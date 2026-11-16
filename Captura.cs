using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Captura
{
    static class Program
    {
        const int MOD_CONTROL = 0x2;
        const int MOD_SHIFT = 0x4;
        const int MOD_NOREPEAT = 0x4000;
        const int VK_SNAPSHOT = 0x2C;
        const int VK_F11 = 0x7A;
        const int WM_HOTKEY = 0x312;

        static Mutex _mutex;
        static NotifyIcon _tray;
        static HotKeyWnd _hk;
        static bool _capturing;
        static Preferences _prefs = Preferences.Load();
        static bool _optionsOpen;
        static int _activeKey;
        static bool _queued;

        [STAThread]
        static void Main(string[] args)
        {
            try { UnsafeNativeMethods.SetProcessDPIAware(); } catch { }
            bool selftest = false;
            bool smoke = false;
            bool capturetest = false;
            bool preview = false;
            foreach (string a in args)
            {
                if (string.Compare(a, "--self-test", StringComparison.OrdinalIgnoreCase) == 0) selftest = true;
                else if (string.Compare(a, "--smoke-test", StringComparison.OrdinalIgnoreCase) == 0) smoke = true;
                else if (string.Compare(a, "--capture-test", StringComparison.OrdinalIgnoreCase) == 0) capturetest = true;
                else if(a == "--preview") preview = true;
            }
            Application.EnableVisualStyles();
            if(Array.IndexOf(args,"--clean-profile-test")>=0){
                if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIINT_DATA_DIR")))throw new Exception("Perfil isolado obrigatório");
                var clean=Preferences.Load();
                if(File.Exists(Preferences.FilePath)||clean.AudioMode!=0||clean.CameraDevice!=""||clean.OutputDevice!=""||clean.MicrophoneDevice!=""||clean.StartWithWindows)throw new Exception("Perfil inicial não está limpo");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"clean-profile-test.txt"),"OK: preferências ausentes, câmera/microfone e inicialização automática desativados");return;
            }
            if(Array.IndexOf(args,"--profile-test")>=0){ProfileTests.Execute();return;}
            if(Array.IndexOf(args,"--av-test")>=0){AvTests.Run();return;}
            if(Array.IndexOf(args,"--ui-test")>=0){RecorderForm.RunUiTests();return;}
            if(Array.IndexOf(args,"--settings-preview")>=0){using(var dialog=new AvOptionsForm(new Preferences())){dialog.Show();Application.DoEvents();using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(Point.Empty,dialog.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-preview.png"));}dialog.Dispose();}return;}
            if(Array.IndexOf(args,"--studio-preview")>=0){RecorderForm.RenderPreview();return;}
            if(Array.IndexOf(args,"--recording-test")>=0){RecordingTests.Run();return;}
            if(Array.IndexOf(args,"--options-preview")>=0){using(var options=new OptionsForm(new Preferences(),delegate(Preferences p){return null;})){options.Show();Application.DoEvents();using(var b=new Bitmap(options.Width,options.Height)){options.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"options-preview.png"),ImageFormat.Png);}options.Close();}return;}
            if(Array.IndexOf(args,"--render-preview")>=0) { CaptureForm.RenderFixture(); return; }
            if(Array.IndexOf(args,"--clipboard-test")>=0) { CaptureForm.ClipboardFixture(); return; }
            if(Array.IndexOf(args,"--verify-clipboard")>=0) { using(Image image=Clipboard.GetImage()) { bool clipboardValid=image!=null && image.Size==new Size(100,80); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"clipboard-test.txt"),clipboardValid?"OK: Ctrl+C fecha captura; imagem 100x80 persiste após encerrar processo e está disponível para colar.":"FALHA clipboard"); Environment.Exit(clipboardValid?0:1); } }
            if (selftest) { SelfTest.Run(); return; }
            if (capturetest) { CaptureTest(); return; }
            if(preview) {
                using(CaptureForm form = new CaptureForm()) {
                    form.Shown += delegate { form.SelectArea(new Rectangle(150,150,900,500)); };
                    form.ShowDialog();
                }
                return;
            }
            bool createdNew;
            _mutex = new Mutex(true, (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIINT_DATA_DIR"))?"Local\\Priint.Desktop":"Local\\Priint.Test."+Guid.NewGuid().ToString("N")), out createdNew);
            if (!createdNew) {if(Array.IndexOf(args,"--background")<0){IntPtr running=UnsafeNativeMethods.FindWindow(null,"Priint.CommandWindow");if(running!=IntPtr.Zero)UnsafeNativeMethods.PostMessage(running,0x801B,new IntPtr(Array.IndexOf(args,"--studio")>=0?2:1),IntPtr.Zero);}return;}
            _tray = new NotifyIcon { Icon = Brand.AppIcon(), Visible = true };
            _tray.Text = "Priint · Ctrl+Shift+PrintScreen";
            _tray.MouseClick += delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) QueueCapture(false); };
            ContextMenuStrip ctx = new ContextMenuStrip { BackColor=Brand.Panel, ForeColor=Brand.Ink, ShowImageMargin=false, Renderer=new ToolStripProfessionalRenderer(new PriintMenuColors()), Font=new Font("Segoe UI",10) };
            ToolStripMenuItem miNew = new ToolStripMenuItem("Nova captura");
            miNew.Click += delegate { QueueCapture(false); };
            ToolStripMenuItem miExit = new ToolStripMenuItem("Sair");
            miExit.Click += delegate { RecordingHub.Exit(); };
            RecordingHub.Print=DoCapture;RecordingHub.Options=OpenOptions;
            ctx.Items.Add("Gravar tela…",null,delegate { RecordingHub.Show(); });
            var stopRecording=ctx.Items.Add("Parar gravação",null,delegate { if(RecordingHub.Active)RecordingHub.Toggle(); });ctx.Opening+=delegate{stopRecording.Enabled=RecordingHub.Active;};

            ctx.Items.Add(new ToolStripSeparator());
            var pauseRecording=ctx.Items.Add("Pausar / retomar",null,delegate{RecordingHub.Pause();});ctx.Opening+=delegate{pauseRecording.Enabled=RecordingHub.Active;};
            ctx.Items.Add("Opções…",null,delegate { OpenOptions(); });

            ctx.Items.Add("Sobre o Priint",null,delegate { MessageBox.Show("Priint 6.0\nSeu recorte. Seu ritmo.\n\nCapturas e anotações locais. Sem conta, sem upload.","Sobre o Priint"); });
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add(miExit);
            _tray.ContextMenuStrip = ctx;
            _hk = new HotKeyWnd();
            _hk.OnHotKey = DoCapture;
            IntPtr handle = _hk.Handle;
            if(!RegisterRecordingKeys(_prefs))MessageBox.Show("Um atalho de gravação está ocupado. Ajuste em Opções > Atalhos.","Priint");
            _activeKey=_prefs.Hotkey;
            bool ok = RegisterKey(_activeKey);
            if (!ok)
            {
                ok = _hk.Register(MOD_CONTROL | MOD_SHIFT, VK_F11);
                _activeKey=(int)(Keys.Control|Keys.Shift|Keys.F11);
                if (ok)
                {
                    _tray.Text = "Priint · Ctrl+Shift+F11";
                }
            }
            if (!ok) _tray.Text = "Priint · sem atalho; use o menu da bandeja";
            else _tray.Text = "Priint · "+Preferences.KeyLabel(_activeKey);
            if (smoke)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-test.txt"), "Tray initialized; hotkey=" + ok);
                Timer t = new Timer { Interval = 2000 };
                t.Tick += delegate { t.Stop(); t.Dispose(); Application.Exit(); };
                t.Start();
            }
            else if(Array.IndexOf(args,"--studio")>=0){RecordingHub.Show();}
            else if(_prefs.CaptureOnLaunch && Array.IndexOf(args,"--background")<0) {
                Timer firstCapture = new Timer { Interval = 350 };
                firstCapture.Tick += delegate { firstCapture.Stop(); firstCapture.Dispose(); DoCapture(); };
                firstCapture.Start();
            }
            Icon normalIcon=_tray.Icon;Icon recordingIcon=RecordingTrayIcon.Create();Icon pausedIcon=RecordingTrayIcon.Create(true);bool recordingState=false,pausedState=false;Timer trayState=new Timer{Interval=600};trayState.Tick+=delegate{bool active=RecordingHub.Active;if(active!=recordingState||RecordingHub.Paused!=pausedState){recordingState=active;pausedState=RecordingHub.Paused;_tray.Icon=active?(pausedState?pausedIcon:recordingIcon):normalIcon;}_tray.Text=active?(RecordingHub.Paused?"Priint · PAUSADO":"Priint · GRAVANDO"):"Priint · "+Preferences.KeyLabel(_activeKey);};trayState.Start();
            Application.Run();trayState.Stop();trayState.Dispose();
            _hk.Unregister(); _hk.Dispose();
            _tray.Visible = false; _tray.Dispose(); ctx.Dispose();normalIcon.Dispose();recordingIcon.Dispose();pausedIcon.Dispose();
            _mutex.ReleaseMutex(); _mutex.Dispose();
        }

        static bool RegisterNamedKey(int id,int key){Keys k=(Keys)key;int mods=MOD_NOREPEAT;if((k&Keys.Control)!=0)mods|=2;if((k&Keys.Shift)!=0)mods|=4;if((k&Keys.Alt)!=0)mods|=1;return UnsafeNativeMethods.RegisterHotKey(_hk.Handle,id,mods,(int)(k&Keys.KeyCode));}
        static bool RegisterRecordingKeys(Preferences p){for(int id=2;id<=4;id++)UnsafeNativeMethods.UnregisterHotKey(_hk.Handle,id);bool a=RegisterNamedKey(2,p.RecordKey),b=RegisterNamedKey(3,p.PauseKey),c=RegisterNamedKey(4,p.StopKey);return a&&b&&c;}        static bool RegisterKey(int key) { Keys k=(Keys)key;int mods=0;if((k&Keys.Control)!=0)mods|=2;if((k&Keys.Shift)!=0)mods|=4;if((k&Keys.Alt)!=0)mods|=1;return _hk.Register(mods,(int)(k&Keys.KeyCode)); }
        static void OpenOptions(){if(_optionsOpen||_capturing)return;_optionsOpen=true;_prefs=Preferences.Load();try{using(var form=new OptionsForm(_prefs,delegate(Preferences next){int previous=_activeKey;var distinct=new HashSet<int>{next.Hotkey,next.RecordKey,next.PauseKey,next.StopKey};if(distinct.Count!=4)return "Use uma combinação diferente para cada comando.";for(int id=2;id<=4;id++)UnsafeNativeMethods.UnregisterHotKey(_hk.Handle,id);if(!RegisterKey(next.Hotkey)||!RegisterRecordingKeys(next)){for(int id=2;id<=4;id++)UnsafeNativeMethods.UnregisterHotKey(_hk.Handle,id);RegisterKey(previous);RegisterRecordingKeys(_prefs);return "Atalho ocupado. Escolha outra combinação.";}bool oldStartup=Startup.Enabled;try{Startup.Set(next.StartWithWindows);next.Save();}catch(Exception ex){try{Startup.Set(oldStartup);}catch{}for(int id=2;id<=4;id++)UnsafeNativeMethods.UnregisterHotKey(_hk.Handle,id);RegisterKey(previous);RegisterRecordingKeys(_prefs);return "Não foi possível salvar: "+ex.Message;} _prefs=next;RecordingHub.ReloadOptions();_activeKey=next.Hotkey;_tray.Text="Priint · "+Preferences.KeyLabel(_activeKey);return null;}))form.ShowDialog();}finally{_optionsOpen=false;}}
        static void QueueCapture(bool full){if(_capturing||_optionsOpen||_queued)return;_queued=true;Timer delay=new Timer{Interval=220};delay.Tick+=delegate{delay.Stop();delay.Dispose();_queued=false;RunCapture(full);};delay.Start();}
        static void DoCapture(){QueueCapture(false);}
        static void RunCapture(bool full)
        {
            if (_capturing || _optionsOpen || RecordingHub.Active) return;
            _capturing = true;
            try
            {
                if(_prefs.Delay>0){using(var wait=new DelayForm(_prefs.Delay)){if(wait.ShowDialog()!=DialogResult.OK)return;}}
                using (CaptureForm cap = new CaptureForm())
                {
                    if(full) cap.Shown+=delegate{cap.SelectArea(cap.ClientRectangle);};
                    cap.ShowDialog();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { _capturing = false; }
        }

        static void CaptureTest()
        {
            try
            {
                using (CaptureForm form = new CaptureForm())
                {
                    Rectangle expected = SystemInformation.VirtualScreen;
                    Timer timer = new Timer { Interval = 300 };
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        timer.Dispose();
                        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        typeof(CaptureForm).GetMethod("OnMouseDown", flags).Invoke(form, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0) });
                        typeof(CaptureForm).GetMethod("OnMouseUp", flags).Invoke(form, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 120, 100, 0) });
                        if (form.Crop == null || form.Crop.Width != 100 || form.Crop.Height != 80)
                            throw new Exception("Crop incorreto: " + (form.Crop == null ? "nulo" : form.Crop.Width + "x" + form.Crop.Height));
                        if (form.Bounds != expected) throw new Exception("Geometria incorreta.");
                        form.DialogResult = DialogResult.Cancel;
                        form.Close();
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "capture-test.txt"), "OK: crop 100x80, VirtualScreen, sem salvar pixels.");
                    };
                    form.Shown += delegate { timer.Start(); };
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "capture-test.txt"), ex.ToString());
                Environment.Exit(1);
            }
        }

        enum Tool { Caneta, Linha, Seta, Retangulo, Texto, Tarja }

        class Shape
        {
            public Tool Kind;
            public Color Color = Color.Red;
            public int Thickness = 2;
            public List<Point> Points = new List<Point>();
            public string Text = "";
        }

        static Bitmap MakeBlur(Bitmap src, int factor)
        {
            int w = Math.Max(1, src.Width / factor);
            int h = Math.Max(1, src.Height / factor);
            Bitmap small = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(src, 0, 0, w, h); }
            Bitmap blur = new Bitmap(src.Width, src.Height);
            using (Graphics g = Graphics.FromImage(blur)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(small, 0, 0, src.Width, src.Height); }
            small.Dispose();
            return blur;
        }

        static Bitmap CropBitmap(Bitmap src, Rectangle rect)
        {
            Rectangle clipped = Rectangle.Intersect(rect, new Rectangle(Point.Empty, src.Size));
            if (clipped.Width <= 0 || clipped.Height <= 0) throw new ArgumentException("Seleção vazia.");
            return src.Clone(clipped, PixelFormat.Format32bppArgb);
        }

        static void DrawShape(Graphics g, Shape sh)
        {
            using (Pen pen = new Pen(sh.Color, sh.Thickness))
            using (Brush brush = new SolidBrush(sh.Color))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                switch (sh.Kind)
                {
                    case Tool.Caneta:
                        if (sh.Points.Count == 1) g.FillEllipse(brush, sh.Points[0].X, sh.Points[0].Y, sh.Thickness, sh.Thickness);
                        for (int i = 1; i < sh.Points.Count; i++) g.DrawLine(pen, sh.Points[i - 1], sh.Points[i]);
                        break;
                    case Tool.Linha:
                        g.DrawLine(pen, sh.Points[0], sh.Points[1]);
                        break;
                    case Tool.Seta:
                        DrawArrow(g, pen, sh.Points[0], sh.Points[1]);
                        break;
                    case Tool.Retangulo:
                        g.DrawRectangle(pen, BoundsOf(sh.Points[0], sh.Points[1]));
                        break;
                    case Tool.Texto:
                        using(Font font = new Font("Segoe UI",Math.Max(14,sh.Thickness*3),FontStyle.Regular,GraphicsUnit.Pixel)) g.DrawString(sh.Text, font, brush, (float)sh.Points[0].X, (float)sh.Points[0].Y);
                        break;
                    case Tool.Tarja:
                        g.FillRectangle(brush, BoundsOf(sh.Points[0], sh.Points[1]));
                        break;
                }
            }
        }

        static Rectangle BoundsOf(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        static void DrawArrow(Graphics g, Pen pen, Point p, Point q)
        {
            g.DrawLine(pen, p, q);
            double ang = Math.Atan2(q.Y - p.Y, q.X - p.X);
            double len = 10.0;
            PointF a1 = new PointF((float)(q.X - len * Math.Cos(ang) + len * 0.5 * Math.Sin(ang)), (float)(q.Y - len * Math.Sin(ang) - len * 0.5 * Math.Cos(ang)));
            PointF a2 = new PointF((float)(q.X - len * Math.Cos(ang) - len * 0.5 * Math.Sin(ang)), (float)(q.Y - len * Math.Sin(ang) + len * 0.5 * Math.Cos(ang)));
            g.DrawLine(pen, q, a1);
            g.DrawLine(pen, q, a2);
        }

        class HotKeyWnd : Form
        {
            int _id = 1;
            public Action OnHotKey;
            public HotKeyWnd()
            {
                Text="Priint.CommandWindow";
                ShowInTaskbar = false;
            }
            public bool Register(int mods, int vk)
            {
                Unregister();
                _id = 1;
                return UnsafeNativeMethods.RegisterHotKey(Handle, _id, mods | MOD_NOREPEAT, vk);
            }
            public void Unregister()
            {
                if (_id != 0)
                {
                    UnsafeNativeMethods.UnregisterHotKey(Handle, _id);
                    _id = 0;
                }
            }
            protected override void WndProc(ref Message m)
            {
                if(m.Msg==0x801B){if((int)m.WParam==2)RecordingHub.Show();else DoCapture();return;}
                if(m.Msg==WM_HOTKEY && (int)m.WParam==3){RecordingHub.Pause();return;}if(m.Msg==WM_HOTKEY && (int)m.WParam==4){RecordingHub.Stop();return;}
                if(m.Msg==WM_HOTKEY && (int)m.WParam==2){RecordingHub.Toggle();return;}
                if (m.Msg == WM_HOTKEY && (int)m.WParam == _id)
                {
                    Action h = OnHotKey;
                    if (h != null) h();
                }
                base.WndProc(ref m);
            }
        }

        class CaptureForm : Form
        {
            enum Modo { Selecao, Anotacao }

            Rectangle _vs;
            Bitmap _desktop;
            Bitmap _blur;
            Rectangle _sel;
            Point _start;
            bool _dragging;
            int _resizeDir = -1;
            Modo _modo = Modo.Selecao;
            Bitmap _crop;
            Rectangle _beforeResize;
            bool _moving;
            Point _moveStart;
            List<Shape> _shapes = new List<Shape>();
            Shape _temp;
            Tool _tool = Tool.Caneta;
            Color _color = Color.Red;
            int _thickness = 2;
            Panel _tools;
            Panel _actions;
            ToolTip _tips = new ToolTip();
            Dictionary<Tool, Button> _toolButtons = new Dictionary<Tool, Button>();
            Button _colorButton;
            Font _buttonFont = new Font("Segoe UI Symbol",14);
            const int GripSize = 8;

            public Bitmap Crop { get { return _crop; } }

            public CaptureForm() : this(null) { }
            public CaptureForm(Bitmap fixture)
            {
                _vs = fixture == null ? SystemInformation.VirtualScreen : new Rectangle(0,0,fixture.Width,fixture.Height);
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                Bounds = _vs;
                Cursor = Cursors.Cross;
                TopMost = true;
                KeyPreview = true;
                DoubleBuffered = true;
                _desktop = new Bitmap(_vs.Width, _vs.Height);
                using (Graphics g = Graphics.FromImage(_desktop)) { if(fixture==null) g.CopyFromScreen(_vs.Left, _vs.Top, 0, 0, _vs.Size); else g.DrawImageUnscaled(fixture,0,0); }
                _blur = _prefs.Blur ? MakeBlur(_desktop, _prefs.BlurAmount) : (Bitmap)_desktop.Clone();
                Text = "Priint · selecionar e anotar";
                BuildTools();
                _tools.Visible = false;
                _actions.Visible = false;
            }

            void BuildTools()
            {
                _tools = new Panel { Size = new Size(40, 330), BackColor = Brand.Panel, Padding = new Padding(3) };
                string[] icons = { "✎", "╱", "↗", "▭", "T", "▰" };
                string[] labels = { "Caneta (P)", "Linha (L)", "Seta (A)", "Retângulo (R)", "Texto (T)", "Tarja sólida (B)" };
                foreach (Tool tool in Enum.GetValues(typeof(Tool))) {
                    Tool chosen = tool;
                    Button button = MakeButton(icons[(int)tool], labels[(int)tool], delegate { SelectTool(chosen); });
                    button.Location = new Point(3, 3 + (int)tool * 34);
                    _toolButtons.Add(tool, button); _tools.Controls.Add(button);
                }
                _colorButton = MakeButton("", "Cor", delegate { using (ColorDialog d = new ColorDialog { Color = _color }) { if (d.ShowDialog(this) == DialogResult.OK) { _color = d.Color; _colorButton.BackColor = _color; } } });
                _colorButton.BackColor = _color; _colorButton.Location = new Point(3,210); _tools.Controls.Add(_colorButton);
                NumericUpDown nud = new NumericUpDown { Minimum = 1m, Maximum = 30m, Value = 2m, Width = 36, Location = new Point(2, 249) };
                _tips.SetToolTip(nud, "Espessura do traço / tamanho do texto");
                nud.ValueChanged += delegate { _thickness = (int)nud.Value; };
                _tools.Controls.Add(nud);
                Button undo = MakeButton("↶", "Desfazer (Ctrl+Z)", delegate { Undo(); }); undo.Location = new Point(3,286); _tools.Controls.Add(undo);
                _actions = new Panel { Size = new Size(148,40), BackColor = _tools.BackColor };
                Button[] actions = {
                    MakeButton("⧉", "Copiar e fechar (Ctrl+C / Enter)", delegate { CopyClose(); }),
                    MakeButton("↓", "Salvar PNG (Ctrl+S)", delegate { Save(); }),
                    MakeButton("＋", "Selecionar nova área", delegate { NovaArea(); }),
                    MakeButton("×", "Cancelar (Esc)", delegate { Close(); })
                };
                for(int i=0;i<actions.Length;i++) { actions[i].Location = new Point(4+i*36,3); _actions.Controls.Add(actions[i]); }
                Controls.Add(_tools);
                Controls.Add(_actions);
                SelectTool(Tool.Caneta);
            }

            Button MakeButton(string text, string tip, EventHandler click) {
                Button b = new Button { Text = text, Size = new Size(32,32), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Brand.Panel, Font = _buttonFont, TabStop = false };
                if(tip.StartsWith("Copiar")) { b.Text=""; b.Paint += delegate(object sender, PaintEventArgs e) { e.Graphics.DrawRectangle(Pens.White,9,7,12,15); e.Graphics.DrawRectangle(Pens.White,12,10,12,15); }; }
                if(tip.StartsWith("Salvar")) { b.Text=""; b.Paint += delegate(object sender, PaintEventArgs e) { e.Graphics.DrawRectangle(Pens.White,8,7,17,18); e.Graphics.DrawRectangle(Pens.White,12,7,9,6); e.Graphics.DrawRectangle(Pens.White,12,18,9,7); }; }
                b.FlatAppearance.BorderSize = 0; b.Click += click; _tips.SetToolTip(b,tip); return b;
            }
            void SelectTool(Tool tool) { _tool = tool; foreach(var item in _toolButtons) item.Value.BackColor = item.Key == tool ? Color.FromArgb(70,89,39) : Brand.Panel; Focus(); }

            void EnterAnnotation()
            {
                _modo = Modo.Anotacao;
                _crop = CropBitmap(_desktop, _sel);
                _shapes.Clear();
                _temp = null;
                PositionTools();
                _tools.Visible = true;
                _actions.Visible = true;
                _tools.BringToFront();
                Cursor = Cursors.Default;
                Invalidate();
            }

            void NovaArea()
            {
                _modo = Modo.Selecao;
                _sel = Rectangle.Empty;
                _dragging = false;
                _resizeDir = -1;
                _temp = null;
                _shapes.Clear();
                if (_crop != null) { _crop.Dispose(); _crop = null; }
                _tools.Visible = false;
                _actions.Visible = false;
                Cursor = Cursors.Cross;
                Invalidate();
            }
            public void SelectArea(Rectangle area) { _sel=Rectangle.Intersect(area,ClientRectangle); if(_sel.Width>0 && _sel.Height>0) EnterAnnotation(); }

            void PositionTools()
            {
                if (_tools == null) return;
                Rectangle monitor = Screen.FromPoint(PointToScreen(new Point(_sel.Right-1,_sel.Bottom-1))).Bounds;
                monitor.Offset(-_vs.Left,-_vs.Top);
                int tx = _sel.Right + 8;
                if (tx + _tools.Width > monitor.Right) tx = _sel.Left - _tools.Width - 8;
                int ty = _sel.Bottom - _tools.Height;
                _tools.Location = new Point(Math.Max(monitor.Left,Math.Min(tx,monitor.Right-_tools.Width)),Math.Max(monitor.Top,Math.Min(ty,monitor.Bottom-_tools.Height)));
                int ax = _sel.Right - _actions.Width;
                int ay = _sel.Bottom + 8;
                if(ay + _actions.Height > monitor.Bottom) ay = _sel.Top-_actions.Height-8;
                _actions.Location = new Point(Math.Max(monitor.Left,Math.Min(ax,monitor.Right-_actions.Width)),Math.Max(monitor.Top,Math.Min(ay,monitor.Bottom-_actions.Height)));
            }

            bool InsideSel(Point p)
            {
                return _sel.Contains(p);
            }

            Point ToCrop(Point client)
            {
                Point p = new Point(client.X - _sel.X, client.Y - _sel.Y);
                p.X = Math.Max(0, Math.Min(_crop.Width - 1, p.X));
                p.Y = Math.Max(0, Math.Min(_crop.Height - 1, p.Y));
                return p;
            }

            int HitTestHandle(Point p)
            {
                int h = GripSize;
                int left = _sel.Left;
                int top = _sel.Top;
                int right = _sel.Right;
                int bottom = _sel.Bottom;
                if (Math.Abs(p.X - left) <= h && Math.Abs(p.Y - top) <= h) return 0;
                if (Math.Abs(p.X - (left + _sel.Width / 2)) <= h && Math.Abs(p.Y - top) <= h) return 1;
                if (Math.Abs(p.X - right) <= h && Math.Abs(p.Y - top) <= h) return 2;
                if (Math.Abs(p.X - left) <= h && Math.Abs(p.Y - (top + _sel.Height / 2)) <= h) return 3;
                if (Math.Abs(p.X - right) <= h && Math.Abs(p.Y - (top + _sel.Height / 2)) <= h) return 4;
                if (Math.Abs(p.X - left) <= h && Math.Abs(p.Y - bottom) <= h) return 5;
                if (Math.Abs(p.X - (left + _sel.Width / 2)) <= h && Math.Abs(p.Y - bottom) <= h) return 6;
                if (Math.Abs(p.X - right) <= h && Math.Abs(p.Y - bottom) <= h) return 7;
                return -1;
            }

            Cursor DirCursor(int dir)
            {
                switch (dir)
                {
                    case 0: return Cursors.SizeNWSE;
                    case 1: return Cursors.SizeNS;
                    case 2: return Cursors.SizeNESW;
                    case 3: return Cursors.SizeWE;
                    case 4: return Cursors.SizeWE;
                    case 5: return Cursors.SizeNESW;
                    case 6: return Cursors.SizeNS;
                    case 7: return Cursors.SizeNWSE;
                    default: return Cursors.Cross;
                }
            }

            void ClampSel()
            {
                _sel = NormalizeRect(_sel);
                _sel = Rectangle.Intersect(_sel, ClientRectangle);
            }

            static Rectangle NormalizeRect(Rectangle r)
            {
                if (r.Width < 0) { r.X += r.Width; r.Width = -r.Width; }
                if (r.Height < 0) { r.Y += r.Height; r.Height = -r.Height; }
                return r;
            }

            static Rectangle Normalize(Point a, Point b)
            {
                int x = Math.Min(a.X, b.X);
                int y = Math.Min(a.Y, b.Y);
                return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            }

            void ResizeSelection(Point cur)
            {
                int left = _sel.X;
                int top = _sel.Y;
                int right = _sel.Right;
                int bottom = _sel.Bottom;
                int x = left;
                int y = top;
                int w = right - left;
                int h = bottom - top;
                switch (_resizeDir)
                {
                    case 0: x = cur.X; y = cur.Y; w = right - x; h = bottom - y; break;
                    case 1: y = cur.Y; h = bottom - y; break;
                    case 2: y = cur.Y; h = bottom - y; w = cur.X - left; break;
                    case 3: x = cur.X; w = right - x; break;
                    case 4: w = cur.X - left; break;
                    case 5: x = cur.X; w = right - x; h = cur.Y - top; break;
                    case 6: h = cur.Y - top; break;
                    case 7: w = cur.X - left; h = cur.Y - top; break;
                }
                Rectangle r = NormalizeRect(new Rectangle(x, y, w, h));
                r = Rectangle.Intersect(r, ClientRectangle);
                if (r.Width >= GripSize && r.Height >= GripSize) _sel = r;
                PositionTools(); Invalidate();
            }

            void UpdateTemp(Point p)
            {
                if (_tool == Tool.Caneta) _temp.Points.Add(p);
                else if (_temp.Points.Count > 1) _temp.Points[_temp.Points.Count - 1] = p;
            }

            void Undo()
            {
                if (_shapes.Count > 0)
                {
                    _shapes.RemoveAt(_shapes.Count - 1);
                    Invalidate();
                }
            }

            void CopyClose()
            {
                try
                {
                    using (Bitmap bmp = RenderFinal()) Clipboard.SetDataObject(bmp, true);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Erro ao copiar", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                DialogResult = DialogResult.OK;
                Close();
            }

            void Save()
            {
                try
                {
                    using (SaveFileDialog d = new SaveFileDialog { Filter = "PNG · sem perda|*.png|JPEG · arquivo menor|*.jpg", FilterIndex=_prefs.Format=="JPEG"?2:1, FileName = "Priint-"+DateTime.Now.ToString("yyyy-MM-dd-HHmmss"), AddExtension=true, DefaultExt=_prefs.Format=="JPEG"?"jpg":"png", InitialDirectory=Directory.Exists(_prefs.Folder)?_prefs.Folder:"" })
                    {
                        if (d.ShowDialog(this) == DialogResult.OK)
                        {
                            using (Bitmap bmp = RenderFinal()) {Preferences.Export(bmp,d.FileName,d.FilterIndex==2,_prefs.Quality);if(_prefs.CopyAfterSave)Clipboard.SetDataObject(bmp,true);}
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    }
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Erro ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }

            Bitmap RenderFinal()
            {
                Bitmap bmp = new Bitmap(_crop.Width, _crop.Height);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImageUnscaled(_crop, 0, 0);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    foreach (Shape sh in _shapes) DrawShape(g, sh);
                    if (_temp != null) DrawShape(g, _temp);
                }
                return bmp;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return; }
                if (_modo == Modo.Anotacao)
                {
                    if (e.KeyCode == Keys.Enter) { CopyClose(); e.SuppressKeyPress = true; return; }
                    if (e.Control && e.KeyCode == Keys.C) { CopyClose(); e.SuppressKeyPress = true; return; }
                    if (e.Control && e.KeyCode == Keys.S) { Save(); e.SuppressKeyPress = true; return; }
                    if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.SuppressKeyPress = true; return; }
                    if (!e.Control && !e.Alt) {
                        if(e.KeyCode==Keys.P) SelectTool(Tool.Caneta);
                        if(e.KeyCode==Keys.L) SelectTool(Tool.Linha);
                        if(e.KeyCode==Keys.A) SelectTool(Tool.Seta);
                        if(e.KeyCode==Keys.R) SelectTool(Tool.Retangulo);
                        if(e.KeyCode==Keys.T) SelectTool(Tool.Texto);
                        if(e.KeyCode==Keys.B) SelectTool(Tool.Tarja);
                    }
                }
                base.OnKeyDown(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
                _resizeDir = -1;
                if(_modo == Modo.Anotacao) {
                    _resizeDir = HitTestHandle(e.Location);
                    if (_resizeDir >= 0) { _beforeResize = _sel; _dragging=true; Capture=true; return; }
                    if (InsideSel(e.Location) && (ModifierKeys & Keys.Shift) != 0) { _beforeResize=_sel; _moveStart=e.Location; _moving=true; _dragging=true; Capture=true; return; }
                }
                if (_modo == Modo.Selecao)
                {
                    _resizeDir = HitTestHandle(e.Location);
                    _dragging = true;
                    Capture = true;
                    _start = e.Location;
                    if (_resizeDir < 0) _sel = new Rectangle(e.X, e.Y, 0, 0);
                    Invalidate();
                    return;
                }
                if (InsideSel(e.Location))
                {
                    _dragging = true;
                    Capture = true;
                    Point p = ToCrop(e.Location);
                    _temp = new Shape { Kind = _tool, Color = _color, Thickness = _thickness };
                    _temp.Points.Add(p);
                    if (_tool != Tool.Caneta && _tool != Tool.Texto) _temp.Points.Add(new Point(p.X, p.Y));
                    if (_tool == Tool.Texto)
                    {
                        _dragging = false;
                        Capture = false;
                        using (TextDialog d = new TextDialog())
                        {
                            if (d.ShowDialog(this) != DialogResult.OK) { _temp = null; return; }
                            _temp.Text = d.TextValue;
                        }
                        if (!string.IsNullOrEmpty(_temp.Text)) _shapes.Add(_temp);
                        _temp = null;
                        Invalidate();
                        return;
                    }
                    Invalidate();
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (_dragging)
                {
                    if (_moving) { _sel.Location = new Point(Math.Max(0,Math.Min(ClientSize.Width-_sel.Width,_beforeResize.X+e.X-_moveStart.X)),Math.Max(0,Math.Min(ClientSize.Height-_sel.Height,_beforeResize.Y+e.Y-_moveStart.Y))); PositionTools(); Invalidate(); }
                    else if (_resizeDir >= 0) { ResizeSelection(e.Location); }
                    else if (_modo == Modo.Selecao) { _sel = Normalize(_start, e.Location); ClampSel(); Invalidate(); }
                    else { Point p = ToCrop(e.Location); UpdateTemp(p); Invalidate(); }
                }
                else
                {
                    if (_sel.Width > 0 && _sel.Height > 0 && HitTestHandle(e.Location)>=0) Cursor = DirCursor(HitTestHandle(e.Location));
                    else Cursor = Cursors.Cross;
                }
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (_temp != null && e.Button == MouseButtons.Left) { UpdateTemp(ToCrop(e.Location)); _shapes.Add(_temp); _temp = null; Invalidate(); }
                if (_dragging && e.Button == MouseButtons.Left)
                {
                    _dragging = false;
                    Capture = false;
                    if(_resizeDir>=0) ResizeSelection(e.Location);
                    if (_modo == Modo.Anotacao && (_resizeDir >= 0 || _moving)) {
                        if (_crop != null) _crop.Dispose(); _crop = CropBitmap(_desktop,_sel);
                        int dx=_beforeResize.Left-_sel.Left, dy=_beforeResize.Top-_sel.Top;
                        if(!_moving) foreach(Shape shape in _shapes) for(int i=0;i<shape.Points.Count;i++) shape.Points[i]=new Point(shape.Points[i].X+dx,shape.Points[i].Y+dy);
                    }
                    _moving=false;
                    if (_resizeDir >= 0)
                    {
                        ClampSel();
                    }
                    else if (_modo == Modo.Selecao)
                    {
                        _sel = Normalize(_start, e.Location);
                        ClampSel();
                        if (_sel.Width > 0 && _sel.Height > 0) EnterAnnotation();
                    }
                    Invalidate();
                    _resizeDir=-1;
                }
                base.OnMouseUp(e);
            }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (_desktop == null) return;
                Graphics g = e.Graphics;
                g.DrawImage(_blur, ClientRectangle);
                using(Brush dim = new SolidBrush(Color.FromArgb(_prefs.Dim,0,0,0))) g.FillRectangle(dim,ClientRectangle);
                if(_sel.Width==0 || _sel.Height==0) {
                    string hint="Arraste para selecionar • Esc cancela";
                    Point screenCursor=PointToClient(Cursor.Position);
                    int hintX=Math.Max(12,Math.Min(screenCursor.X+18,ClientSize.Width-330));
                    int hintY=Math.Max(12,Math.Min(screenCursor.Y+24,ClientSize.Height-50));
                    g.FillRectangle(Brushes.Black,hintX,hintY,305,32);
                    g.DrawString(hint,SystemFonts.MessageBoxFont,Brushes.White,hintX+10,hintY+8);
                }
                if (_sel.Width > 0 && _sel.Height > 0)
                {
                    g.SetClip(_sel);
                    g.DrawImageUnscaled(_desktop, 0, 0);
                    g.ResetClip();
                    if (_modo == Modo.Anotacao)
                    {
                        g.SetClip(_sel);
                        g.TranslateTransform(_sel.X, _sel.Y);
                        g.SmoothingMode=SmoothingMode.AntiAlias;
                        foreach (Shape sh in _shapes) DrawShape(g, sh);
                        if (_temp != null) DrawShape(g, _temp);
                        g.ResetTransform();
                        g.ResetClip();
                    }
                    using (Pen cyan = new Pen(Brand.Lime, 2))
                    using (Pen white = new Pen(Color.White, 1))
                    {
                        g.DrawRectangle(cyan, _sel);
                        Rectangle inner = _sel;
                        inner.Inflate(-1, -1);
                        if (inner.Width > 0 && inner.Height > 0) g.DrawRectangle(white, inner);
                    }
                    string dim = _sel.Width + " x " + _sel.Height;
                    SizeF ms = g.MeasureString(dim, SystemFonts.DefaultFont);
                    RectangleF dp = new RectangleF(_sel.Right + 4, _sel.Bottom + 4, ms.Width, ms.Height);
                    if (dp.Right > _vs.Right) dp.X = _sel.Left - ms.Width - 4;
                    if (dp.Bottom > _vs.Bottom) dp.Y = _sel.Top - ms.Height - 4;
                    g.FillRectangle(Brushes.Black, dp);
                    g.DrawString(dim, SystemFonts.DefaultFont, Brushes.Cyan, dp);
                    {
                        using (Pen hp = new Pen(Brand.Lime, 1))
                        {
                            int h = GripSize / 2;
                            int left = _sel.X;
                            int top = _sel.Y;
                            int right = _sel.Right;
                            int bottom = _sel.Bottom;
                            int midX = left + _sel.Width / 2;
                            int midY = top + _sel.Height / 2;
                            Point[] pts = new Point[] { new Point(left - h, top - h), new Point(midX - h, top - h), new Point(right - h, top - h), new Point(left - h, midY - h), new Point(right - h, midY - h), new Point(left - h, bottom - h), new Point(midX - h, bottom - h), new Point(right - h, bottom - h) };
                            foreach (Point pt in pts)
                            {
                                Rectangle hr = new Rectangle(pt.X, pt.Y, GripSize, GripSize);
                                g.FillRectangle(Brushes.White, hr);
                                g.DrawRectangle(hp, hr);
                            }
                        }
                    }
                }
                base.OnPaint(e);
            }

            public static void VerifyFixture() {
                using(Bitmap fixture = new Bitmap(400,300)) {
                    using(Graphics g=Graphics.FromImage(fixture)) g.Clear(Color.Blue);
                    using(CaptureForm form=new CaptureForm(fixture)) {
                        form.SelectArea(new Rectangle(50,50,100,80));
                        form._tool=Tool.Tarja; form._color=Color.Black;
                        form.OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,65,65,0));
                        form.OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,90,90,0));
                        using(Bitmap result=form.RenderFinal()) {
                            if(result.GetPixel(20,20).ToArgb()!=Color.Black.ToArgb() || result.GetPixel(60,60).ToArgb()!=Color.Blue.ToArgb()) throw new Exception("Render real da tarja incorreto");
                        }
                        form.OnKeyDown(new KeyEventArgs(Keys.Control|Keys.Z));
                        using(Bitmap result=form.RenderFinal()) if(result.GetPixel(20,20).ToArgb()!=Color.Blue.ToArgb()) throw new Exception("CtrlZ real falhou");
                        form.OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,50,50,0));
                        form.OnMouseMove(new MouseEventArgs(MouseButtons.Left,1,40,40,0));
                        form.OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,40,40,0));
                        using(Bitmap result=form.RenderFinal()) if(result.Size!=new Size(110,90)) throw new Exception("Resize real falhou");
                        form._tool=Tool.Seta;
                        form.OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,60,60,0));
                        form.OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,100,100,0));
                        using(Bitmap result=form.RenderFinal()) if(result.GetPixel(40,40).ToArgb()==Color.Blue.ToArgb()) throw new Exception("Seta real falhou");
                    }
                }
            }

            public static void ClipboardFixture() {
                using(Bitmap fixture=new Bitmap(400,300)) {
                    using(Graphics g=Graphics.FromImage(fixture)) g.Clear(Color.Blue);
                    using(CaptureForm form=new CaptureForm(fixture)) {
                        form.SelectArea(new Rectangle(50,50,100,80));
                        form.OnKeyDown(new KeyEventArgs(Keys.Control|Keys.C));
                        if(form.DialogResult!=DialogResult.OK) Environment.Exit(1);
                    }
                }
            }
            public static void RenderFixture() {
                using(Bitmap fixture=new Bitmap(1280,720)) {
                    using(Graphics g=Graphics.FromImage(fixture)) {
                        using(LinearGradientBrush gradient=new LinearGradientBrush(new Rectangle(0,0,1280,720),Color.FromArgb(30,65,140),Color.FromArgb(14,160,175),25)) g.FillRectangle(gradient,0,0,1280,720);
                        for(int x=0;x<1280;x+=40) g.DrawLine(Pens.LightSteelBlue,x,0,x,720);
                        for(int y=0;y<720;y+=40) g.DrawLine(Pens.LightSteelBlue,0,y,1280,y);
                        using(Font font=new Font("Segoe UI",30)) g.DrawString("Priint · área nítida",font,Brushes.White,240,200);
                    }
                    using(CaptureForm form=new CaptureForm(fixture)) {
                        form.SelectArea(new Rectangle(180,120,800,450));
                        form._shapes.Add(new Shape { Kind=Tool.Seta, Color=Color.Yellow, Thickness=4, Points=new List<Point>{new Point(150,200),new Point(480,300)} });
                        form.Show(); form.PerformLayout(); Application.DoEvents();
                        using(Bitmap preview=new Bitmap(1280,720)) { form.DrawToBitmap(preview,new Rectangle(0,0,1280,720)); preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-fixture.png"),ImageFormat.Png); }
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (_desktop != null) { _desktop.Dispose(); _desktop = null; }
                    if (_blur != null) { _blur.Dispose(); _blur = null; }
                    if (_crop != null) { _crop.Dispose(); _crop = null; }
                    if (_tools != null) { _tools.Dispose(); _tools = null; }
                    if (_actions != null) { _actions.Dispose(); _actions = null; }
                    _tips.Dispose();
                    _buttonFont.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        class TextDialog : Form
        {
            public string TextValue = "";
            public TextDialog()
            {
                Text = "Texto";
                ClientSize = new Size(350, 115);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                TextBox tb = new TextBox { Location = new Point(10, 10), Size = new Size(300, 50), Multiline = true };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(180, 75), Size = new Size(75, 23) };
                Button cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(265, 75), Size = new Size(75, 23) };
                Controls.Add(tb);
                Controls.Add(ok);
                Controls.Add(cancel);
                AcceptButton = ok;
                CancelButton = cancel;
                tb.TextChanged += delegate { TextValue = tb.Text; };
            }
        }

        static class SelfTest
        {
            public static void Run()
            {
                List<string> log = new List<string>();
                int fails = 0;
                Bitmap bmp = null;
                try
                {
                    ProductTest.Verify(); log.Add("OK: preferências persistidas, atualização, recuperação, PNG/JPEG reais, ícone embutido"); CaptureForm.VerifyFixture();
                    log.Add("OK: produção real — tarja, Ctrl+Z, alças de resize, seta");
                    bmp = new Bitmap(100, 100);
                    using (Graphics g = Graphics.FromImage(bmp)) g.FillRectangle(Brushes.Blue, 0, 0, 100, 100);
                    using (Graphics g = Graphics.FromImage(bmp)) g.FillRectangle(Brushes.Red, 0, 0, 8, 8);
                    using (Graphics g = Graphics.FromImage(bmp)) g.FillRectangle(Brushes.Green, 20, 20, 40, 40);
                    Rectangle r = new Rectangle(10, 10, 40, 30);
                    Bitmap crop = CropBitmap(bmp, r);
                    Check(log, ref fails, crop.Width == 40 && crop.Height == 30, "crop tamanho");
                    Check(log, ref fails, crop.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(), "crop conteudo");
                    crop.Dispose();
                    Bitmap blur = MakeBlur(bmp, 4);
                    Bitmap comp = new Bitmap(bmp.Width, bmp.Height);
                    using (Graphics g = Graphics.FromImage(comp))
                    {
                        g.DrawImage(blur, 0, 0);
                        g.SetClip(r);
                        g.DrawImageUnscaled(bmp, 0, 0);
                        g.ResetClip();
                    }
                    Check(log, ref fails, comp.GetPixel(25, 25).ToArgb() == Color.Green.ToArgb(), "blur interior nitido preservado");
                    Check(log, ref fails, comp.GetPixel(8, 4).ToArgb() != bmp.GetPixel(8, 4).ToArgb(), "blur exterior alterado");
                    comp.Dispose();
                    blur.Dispose();
                    List<Shape> shapes = new List<Shape>();
                    shapes.Add(new Shape { Kind = Tool.Tarja, Color = Color.Red, Points = new List<Point> { new Point(0, 0), new Point(20, 20) } });
                    Bitmap canvas = RenderShapes(bmp, shapes);
                    Check(log, ref fails, canvas.Width == 100 && canvas.Height == 100, "render tamanho");
                    Check(log, ref fails, canvas.GetPixel(5, 5).ToArgb() == Color.Red.ToArgb(), "render tarja solida");
                    shapes.RemoveAt(0);
                    Bitmap empty = RenderShapes(bmp, new List<Shape>());
                    Check(log, ref fails, empty.GetPixel(15, 15).ToArgb() == Color.Blue.ToArgb(), "undo remove");
                    shapes.Add(new Shape { Kind = Tool.Seta, Color = Color.Black, Points = new List<Point> { new Point(5, 5), new Point(25, 25) } });
                    using (MemoryStream ms = new MemoryStream()) { RenderShapes(bmp, shapes).Save(ms, ImageFormat.Png); ms.Position = 0; using (Bitmap loaded = new Bitmap(ms)) Check(log, ref fails, loaded.Width == 100 && loaded.GetPixel(10, 10).ToArgb() == Color.Black.ToArgb(), "PNG roundtrip"); }
                    canvas.Dispose();
                    empty.Dispose();
                }
                catch (Exception ex)
                {
                    fails++;
                    log.Add("EXCECAO: " + ex.Message);
                }
                finally
                {
                    if (bmp != null) bmp.Dispose();
                }
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt");
                log.Add("");
                log.Add("RESULTADO=" + (fails == 0 ? "SUCESSO" : "FALHA"));
                try { File.WriteAllText(path, string.Join(Environment.NewLine, log.ToArray())); } catch { }
                Environment.Exit(fails == 0 ? 0 : 1);
            }

            static void Check(List<string> log, ref int fails, bool cond, string msg)
            {
                if (cond) log.Add("OK: " + msg);
                else { fails++; log.Add("FALHA: " + msg); }
            }

            static Bitmap RenderShapes(Bitmap baseImg, List<Shape> shapes)
            {
                Bitmap bmp = new Bitmap(baseImg.Width, baseImg.Height);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.DrawImage(baseImg, 0, 0);
                    foreach (Shape sh in shapes) DrawShape(g, sh);
                }
                return bmp;
            }
        }

        class UnsafeNativeMethods
        {
            [DllImport("user32.dll")]
            public static extern bool SetProcessDPIAware();
            [DllImport("user32.dll")]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
            [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls,string title);
            [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
            [DllImport("user32.dll")]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }
    }
}





