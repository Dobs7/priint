﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
namespace Captura {
 public class Preferences {
  public bool Blur=true, CaptureOnLaunch=true, CopyAfterSave=false, StartWithWindows=false;
  public int BlurAmount=9, Dim=48, Delay=0, Quality=92;
  public int AudioMode=0,CameraWidth=640,CameraHeight=480;public string OutputDevice="",MicrophoneDevice="",CameraShape="Retangular",CameraDevice="";public float CameraZoom=1,CameraX=.72f,CameraY=.68f;public bool CameraMirror=false,CameraTracking=false,LivePreview=true;public int RecordKey=(int)(Keys.Control|Keys.Shift|Keys.F9),PauseKey=(int)(Keys.Control|Keys.Shift|Keys.F8),StopKey=(int)(Keys.Control|Keys.Shift|Keys.F10);
  public int VideoGpu=-1,VideoMinMbps=12,VideoMaxMbps=24;public string VideoCodec="H.264",VideoContainer="MP4",VideoResolution="Original",VideoQuality="Equilibrada";public string CameraInput="Automático";public bool SeparateMp3=false;public int VideoFps=30; public bool VideoNvenc=false,VideoCursor=true; public string VideoFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"Priint");
  public int Hotkey=(int)(Keys.Control|Keys.Shift|Keys.PrintScreen);
  public string Format="PNG", Folder="";
  public Preferences Clone(){return (Preferences)MemberwiseClone();}
  public static string FilePath {get{return Path.Combine(Environment.GetEnvironmentVariable("PRIINT_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Priint"),"preferences.xml");}}
  public static Preferences Load(){return LoadFrom(FilePath);}
  public static Preferences LoadFrom(string path){try{using(var s=File.OpenRead(path)){var p=(Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(s); p.Validate();return p;}}catch{return new Preferences();}}
  public void Validate(){if(CameraShape!="Quadrada"&&CameraShape!="Redonda")CameraShape="Retangular";AudioMode=Math.Max(0,Math.Min(3,AudioMode));CameraZoom=Math.Max(1,Math.Min(4,CameraZoom));CameraWidth=Math.Max(64,Math.Min(3840,CameraWidth));CameraHeight=Math.Max(64,Math.Min(2160,CameraHeight));VideoFps=VideoProfiles.NormalizeFps(VideoFps);if(VideoNvenc&&VideoGpu<0)VideoGpu=0;if(VideoCodec!="H.265")VideoCodec="H.264";if(VideoContainer!="AVI")VideoContainer="MP4";if(VideoResolution!="720p"&&VideoResolution!="1080p"&&VideoResolution!="1440p"&&VideoResolution!="2160p")VideoResolution="Original";if(VideoQuality!="Alta"&&VideoQuality!="Leve")VideoQuality="Equilibrada";VideoMinMbps=Math.Max(1,Math.Min(500,VideoMinMbps));VideoMaxMbps=Math.Max(VideoMinMbps,Math.Min(500,VideoMaxMbps));if(string.IsNullOrWhiteSpace(VideoFolder))VideoFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"Priint");BlurAmount=Math.Max(2,Math.Min(16,BlurAmount));Dim=Math.Max(0,Math.Min(160,Dim));Delay=Math.Max(0,Math.Min(10,Delay));Quality=Math.Max(30,Math.Min(100,Quality)); if(Format!="JPEG")Format="PNG"; var code=(Keys)Hotkey&Keys.KeyCode;if(code==Keys.None || code==Keys.ControlKey || code==Keys.ShiftKey || code==Keys.Menu)Hotkey=(int)(Keys.Control|Keys.Shift|Keys.PrintScreen);}
  public void Save(){SaveTo(FilePath);}
  public void SaveTo(string path){Validate();string dir=Path.GetDirectoryName(path);Directory.CreateDirectory(dir);string temp=path+".tmp";using(var s=File.Create(temp))new XmlSerializer(typeof(Preferences)).Serialize(s,this);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  public static string KeyLabel(int key){return new KeysConverter().ConvertToString((Keys)key);}
  public static void Export(Bitmap image,string path,bool jpeg,int quality){
   if(!jpeg){image.Save(path,ImageFormat.Png);return;}
   ImageCodecInfo codec=null;foreach(var c in ImageCodecInfo.GetImageEncoders())if(c.MimeType=="image/jpeg")codec=c;
   using(var pars=new EncoderParameters(1)){pars.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)quality);image.Save(path,codec,pars);}
  }
 }
 static class Brand {
  public static readonly Color Back=Color.FromArgb(21,24,26), Panel=Color.FromArgb(31,35,37), Lime=Color.FromArgb(118,185,0), Ink=Color.FromArgb(238,242,232), Muted=Color.FromArgb(167,177,171);
  public static Icon AppIcon(){using(var s=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Priint.ico")){return s==null?(Icon)SystemIcons.Application.Clone():new Icon(s,32,32);}}
  public static Button Button(string text){var b=new Button{Text=text,FlatStyle=FlatStyle.Flat,BackColor=Panel,ForeColor=Ink,Height=38,Width=135,Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=Color.FromArgb(65,72,67);return b;}
 }
 class OptionsForm:Form {
  public Preferences Value;Preferences avDraft; readonly Preferences original; readonly Func<Preferences,string> apply;
  CheckBox blur,launch,copy,startup,videoCursor;FpsChoice videoFps;DarkChoice videoEncoder;TextBox videoFolder; TrackBar dim,amount; NumericUpDown delay,quality; TextBox shortcut,folder,recordShortcut,pauseShortcut,stopShortcut; DarkChoice format; int shortcutValue; Label status;
  public OptionsForm(Preferences current,Func<Preferences,string> commit){original=current;avDraft=current.Clone();Value=current.Clone();Value.StartWithWindows=Startup.Enabled;apply=commit;shortcutValue=Value.Hotkey;
   Text="Priint · Opções";Icon=Brand.AppIcon();BackColor=Brand.Back;ForeColor=Brand.Ink;Font=new Font("Segoe UI",10);ClientSize=new Size(740,540);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterScreen;
   var header=new Label{Text="priint.",Font=new Font("Segoe UI",30,FontStyle.Bold),ForeColor=Brand.Lime,Location=new Point(27,16),AutoSize=true};Controls.Add(header);
   Controls.Add(new Label{Text="SEU RECORTE. SEU RITMO.",ForeColor=Brand.Muted,Location=new Point(31,77),AutoSize=true});
   var tabs=new SectionTabs{Location=new Point(27,116),Size=new Size(686,335),Font=Font};Controls.Add(tabs);
   Panel general=Page(tabs,"Geral"),keys=Page(tabs,"Atalhos"),formats=Page(tabs,"Print"),video=Page(tabs,"Gravação");
   startup=Check(general,"Iniciar com o Windows · ficar na bandeja",Value.StartWithWindows,22,16);
   launch=Check(general,"Abrir uma captura ao iniciar manualmente",Value.CaptureOnLaunch,22,49);
   blur=Check(general,"Desfocar o exterior da seleção",Value.Blur,22,82);
   LabelAt(general,"Desfoque",22,120);amount=Slider(general,Value.BlurAmount,2,16,190,110);LabelAt(general,"Suave → intenso",470,120);
   LabelAt(general,"Escurecimento",22,162);dim=Slider(general,Value.Dim,0,160,190,152);LabelAt(general,"Claro → escuro",470,162);
   LabelAt(general,"Atraso antes da captura",22,211);delay=Number(general,Value.Delay,0,10,238,207);LabelAt(general,"segundos",325,211);
   LabelAt(general,"Só o exterior muda. Seu recorte continua nítido.",22,260,Brand.Muted);
   LabelAt(keys,"Clique no campo e pressione a combinação desejada.",22,14);LabelAt(keys,"Print",22,60);shortcut=new TextBox{Location=new Point(235,54),Width=400,ReadOnly=true,Text=Preferences.KeyLabel(shortcutValue),BackColor=Brand.Back,ForeColor=Brand.Lime};keys.Controls.Add(shortcut);
   shortcut.KeyDown+=delegate(object sender,KeyEventArgs e){e.SuppressKeyPress=true;if(e.KeyCode==Keys.ControlKey||e.KeyCode==Keys.ShiftKey||e.KeyCode==Keys.Menu||e.KeyCode==Keys.None)return;shortcutValue=(int)e.KeyData;shortcut.Text=Preferences.KeyLabel(shortcutValue);};
   LabelAt(keys,"Iniciar / parar gravação",22,104);recordShortcut=KeyInput(keys,Value.RecordKey,235,98);
   LabelAt(keys,"Pausar / retomar",22,148);pauseShortcut=KeyInput(keys,Value.PauseKey,235,142);
   LabelAt(keys,"Parar e salvar",22,192);stopShortcut=KeyInput(keys,Value.StopKey,235,186);
   LabelAt(keys,"Ctrl+C copia · Ctrl+S salva · Ctrl+Z desfaz · Esc cancela o print.\nAtalhos ocupados são avisados ao salvar.",22,247,Brand.Muted);   LabelAt(formats,"Formato padrão",22,22);format=new DarkChoice{Location=new Point(215,18),Width=170,DropDownStyle=ComboBoxStyle.DropDownList};format.Items.AddRange(new object[]{"PNG","JPEG"});format.SelectedItem=Value.Format;formats.Controls.Add(format);
   LabelAt(formats,"Qualidade JPEG",22,69);quality=Number(formats,Value.Quality,30,100,215,65);LabelAt(formats,"% · PNG não perde qualidade",305,69,Brand.Muted);quality.Enabled=Value.Format=="JPEG";format.SelectedIndexChanged+=delegate{quality.Enabled=(string)format.SelectedItem=="JPEG";};
   LabelAt(formats,"Pasta inicial ao salvar",22,119);folder=new TextBox{Location=new Point(22,162),Width=490,Text=Value.Folder,BackColor=Brand.Back,ForeColor=Brand.Ink};formats.Controls.Add(folder);var browse=Brand.Button("Escolher…");browse.SetBounds(523,147,120,32);browse.Click+=delegate{using(var d=new FolderBrowserDialog()){if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;}};formats.Controls.Add(browse);
   copy=Check(formats,"Também copiar a imagem depois de salvar",Value.CopyAfterSave,22,211);LabelAt(formats,"Nenhum print é enviado à internet ou salvo sem sua ação.",22,263,Brand.Muted);
   LabelAt(video,"FPS padrão",22,24);videoFps=new FpsChoice{Location=new Point(216,20),Width=240};videoFps.Value=Value.VideoFps;video.Controls.Add(videoFps);LabelAt(video,"30 / 60 / 120 / 240 · seleção de FPS",22,61,Brand.Muted);
   LabelAt(video,"Processamento",22,107);videoEncoder=new DarkChoice{Location=new Point(216,101),Width=240};videoEncoder.Items.AddRange(VideoProfiles.Encoders().ToArray());videoEncoder.SelectedIndex=0;for(int i=0;i<videoEncoder.Items.Count;i++)if(((EncoderChoice)videoEncoder.Items[i]).Index==(Value.VideoNvenc?Value.VideoGpu:-1))videoEncoder.SelectedIndex=i;video.Controls.Add(videoEncoder);
   videoCursor=Check(video,"Mostrar o ponteiro do mouse no vídeo",Value.VideoCursor,22,152);
   LabelAt(video,"Pasta das gravações",22,195);videoFolder=new TextBox{Text=Value.VideoFolder,Location=new Point(22,225),Width=490,BackColor=Brand.Back,ForeColor=Brand.Ink};video.Controls.Add(videoFolder);var chooseVideo=Brand.Button("Escolher…");chooseVideo.SetBounds(523,220,120,34);chooseVideo.Click+=delegate{using(var d=new FolderBrowserDialog()){if(d.ShowDialog(this)==DialogResult.OK)videoFolder.Text=d.SelectedPath;}};video.Controls.Add(chooseVideo);
   var avButton=Brand.Button("Configurações de gravação");avButton.SetBounds(22,263,310,32);avButton.Click+=delegate{avDraft.VideoFps=(int)videoFps.Value;avDraft.VideoGpu=((EncoderChoice)videoEncoder.SelectedItem).Index;avDraft.VideoNvenc=avDraft.VideoGpu>=0;using(var dialog=new AvOptionsForm(avDraft)){if(dialog.ShowDialog(this)==DialogResult.OK){avDraft=dialog.Result;videoFps.Value=avDraft.VideoFps;for(int i=0;i<videoEncoder.Items.Count;i++)if(((EncoderChoice)videoEncoder.Items[i]).Index==(avDraft.VideoNvenc?avDraft.VideoGpu:-1))videoEncoder.SelectedIndex=i;}}};video.Controls.Add(avButton);
   status=new Label{Location=new Point(28,463),Size=new Size(388,64),ForeColor=Color.FromArgb(255,183,127)};Controls.Add(status);
   var cancel=Brand.Button("Cancelar");cancel.SetBounds(435,476,120,38);cancel.DialogResult=DialogResult.Cancel;Controls.Add(cancel);CancelButton=cancel;
   var save=Brand.Button("Salvar opções");save.SetBounds(567,476,146,38);save.BackColor=Brand.Lime;save.ForeColor=Brand.Back;save.Click+=delegate{Value=avDraft.Clone();Value.RecordKey=(int)recordShortcut.Tag;Value.PauseKey=(int)pauseShortcut.Tag;Value.StopKey=(int)stopShortcut.Tag;Value.VideoFps=(int)videoFps.Value;Value.VideoGpu=((EncoderChoice)videoEncoder.SelectedItem).Index;Value.VideoNvenc=Value.VideoGpu>=0;Value.VideoCursor=videoCursor.Checked;Value.VideoFolder=videoFolder.Text.Trim();Value.StartWithWindows=startup.Checked;Value.Blur=blur.Checked;Value.CaptureOnLaunch=launch.Checked;Value.CopyAfterSave=copy.Checked;Value.BlurAmount=amount.Value;Value.Dim=dim.Value;Value.Delay=(int)delay.Value;Value.Quality=(int)quality.Value;Value.Hotkey=shortcutValue;Value.Format=(string)format.SelectedItem;Value.Folder=folder.Text.Trim(); if(Value.Folder.Length>0&&!Directory.Exists(Value.Folder)){status.Text="Escolha uma pasta que exista.";return;}if(string.IsNullOrWhiteSpace(Value.VideoFolder)){status.Text="Escolha a pasta das gravações.";return;}string error=apply(Value);if(error!=null){status.Text=error;return;}DialogResult=DialogResult.OK;Close();};Controls.Add(save);
  }
  static TextBox KeyInput(Control parent,int key,int x,int y){var box=new TextBox{ReadOnly=true,Location=new Point(x,y),Width=400,Text=Preferences.KeyLabel(key),Tag=key,BackColor=Brand.Back,ForeColor=Brand.Lime};box.KeyDown+=delegate(object sender,KeyEventArgs e){e.SuppressKeyPress=true;if(e.KeyCode==Keys.ControlKey||e.KeyCode==Keys.ShiftKey||e.KeyCode==Keys.Menu)return;box.Tag=(int)e.KeyData;box.Text=Preferences.KeyLabel((int)e.KeyData);};parent.Controls.Add(box);return box;}
  static Panel Page(SectionTabs tabs,string title){return tabs.AddPage(title);}
  static void LabelAt(Control parent,string text,int x,int y){LabelAt(parent,text,x,y,Brand.Ink);}static void LabelAt(Control parent,string text,int x,int y,Color color){parent.Controls.Add(new Label{Text=text,Location=new Point(x,y),AutoSize=true,ForeColor=color});}
  static CheckBox Check(Control parent,string text,bool value,int x,int y){var c=new CheckBox{Text=text,Checked=value,Location=new Point(x,y),AutoSize=true};parent.Controls.Add(c);return c;}
  static TrackBar Slider(Control parent,int value,int min,int max,int x,int y){var c=new TrackBar{Minimum=min,Maximum=max,Value=value,Location=new Point(x,y),Width=260,Height=40,TickStyle=TickStyle.None,BackColor=Brand.Panel};parent.Controls.Add(c);return c;}
  static NumericUpDown Number(Control parent,int value,int min,int max,int x,int y){var c=new NumericUpDown{Minimum=min,Maximum=max,Value=value,Location=new Point(x,y),Width=75,BackColor=Brand.Back,ForeColor=Brand.Ink};parent.Controls.Add(c);return c;}
 }
}
namespace Captura {
 class DelayForm:System.Windows.Forms.Form {
  public DelayForm(int seconds){Text="Priint · preparando";BackColor=Brand.Back;ForeColor=Brand.Lime;ClientSize=new System.Drawing.Size(300,130);FormBorderStyle=System.Windows.Forms.FormBorderStyle.FixedToolWindow;StartPosition=System.Windows.Forms.FormStartPosition.CenterScreen;TopMost=true;KeyPreview=true;var label=new System.Windows.Forms.Label{Text="Captura em "+seconds+"…",Font=new System.Drawing.Font("Segoe UI",22,System.Drawing.FontStyle.Bold),AutoSize=true,Location=new System.Drawing.Point(22,25)};Controls.Add(label);Controls.Add(new System.Windows.Forms.Label{Text="Esc para cancelar",AutoSize=true,Location=new System.Drawing.Point(25,90)});var timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=delegate{seconds--;if(seconds<=0){timer.Stop();Hide();System.Threading.Thread.Sleep(120);DialogResult=System.Windows.Forms.DialogResult.OK;Close();}else label.Text="Captura em "+seconds+"…";};Shown+=delegate{timer.Start();};KeyDown+=delegate(object sender,System.Windows.Forms.KeyEventArgs e){if(e.KeyCode==System.Windows.Forms.Keys.Escape){DialogResult=System.Windows.Forms.DialogResult.Cancel;Close();}};FormClosed+=delegate{timer.Stop();timer.Dispose();};}
 }
}
namespace Captura {
 class SectionTabs:Panel {
  readonly System.Collections.Generic.List<Panel> pages=new System.Collections.Generic.List<Panel>();readonly System.Collections.Generic.List<Button> buttons=new System.Collections.Generic.List<Button>();
  public Panel AddPage(string title){int i=pages.Count;var p=new Panel{Location=new Point(0,42),Size=new Size(Width,Height-42),BackColor=Brand.Panel,ForeColor=Brand.Ink,Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right};var b=Brand.Button(title);b.SetBounds(i*136,0,128,34);b.Click+=delegate{Select(i);};pages.Add(p);buttons.Add(b);Controls.Add(p);Controls.Add(b);Select(0);return p;}
  public void SelectPage(int index){buttons[index].PerformClick();}
  void Select(int index){for(int i=0;i<pages.Count;i++){pages[i].Visible=i==index;buttons[i].BackColor=i==index?Brand.Lime:Brand.Back;buttons[i].ForeColor=i==index?Brand.Back:Brand.Muted;}}
 }
}
namespace Captura {
 static class ProductTest {
  public static void Verify(){string prefix=Path.Combine(Path.GetTempPath(),"Priint-test-"+Guid.NewGuid().ToString("N"));string settings=prefix+".xml",png=prefix+".png",jpg=prefix+".jpg";try{var p=new Preferences{Format="JPEG",Quality=85,Dim=31,BlurAmount=12,Hotkey=(int)Keys.Insert,CopyAfterSave=true,VideoFps=120,VideoNvenc=true,VideoGpu=0,VideoCodec="H.265",VideoResolution="2160p",VideoMinMbps=20,VideoMaxMbps=45,SeparateMp3=true,VideoCursor=false,VideoFolder=Path.GetTempPath(),AudioMode=3,CameraShape="Redonda",CameraZoom=1.8f,CameraTracking=true,CameraMirror=true};p.SaveTo(settings);var loaded=Preferences.LoadFrom(settings);if(loaded.Format!="JPEG"||loaded.Quality!=85||loaded.Hotkey!=(int)Keys.Insert||!loaded.CopyAfterSave||loaded.Dim!=31||loaded.VideoFps!=120||loaded.VideoCodec!="H.265"||loaded.VideoResolution!="2160p"||loaded.VideoMinMbps!=20||loaded.VideoMaxMbps!=45||!loaded.SeparateMp3||!loaded.VideoNvenc||loaded.VideoCursor||loaded.VideoFolder!=Path.GetTempPath()||loaded.AudioMode!=3||loaded.CameraShape!="Redonda"||!loaded.CameraTracking||!loaded.CameraMirror)throw new Exception("Preferências não persistiram");loaded.Quality=96;loaded.SaveTo(settings);if(Preferences.LoadFrom(settings).Quality!=96)throw new Exception("Atualização atômica falhou");File.WriteAllText(settings,"invalid");if(Preferences.LoadFrom(settings).Format!="PNG")throw new Exception("Fallback de configuração corrompida falhou");using(var image=new Bitmap(80,60)){using(var g=Graphics.FromImage(image))g.Clear(Color.Coral);Preferences.Export(image,png,false,90);Preferences.Export(image,jpg,true,85);using(var a=Image.FromFile(png))if(a.RawFormat.Guid!=ImageFormat.Png.Guid||a.Size!=image.Size)throw new Exception("PNG inválido");using(var a=Image.FromFile(jpg))if(a.RawFormat.Guid!=ImageFormat.Jpeg.Guid||a.Size!=image.Size)throw new Exception("JPEG inválido");}using(var icon=Brand.AppIcon())if(icon.Width<16)throw new Exception("Ícone embutido inválido");}finally{foreach(string file in new[]{settings,settings+".tmp",png,jpg})if(File.Exists(file))File.Delete(file);}}
 }
}
namespace Captura {
 class PriintMenuColors:ProfessionalColorTable {
  public override Color ToolStripDropDownBackground{get{return Brand.Panel;}}
  public override Color ImageMarginGradientBegin{get{return Brand.Panel;}}
  public override Color ImageMarginGradientMiddle{get{return Brand.Panel;}}
  public override Color ImageMarginGradientEnd{get{return Brand.Panel;}}
  public override Color MenuBorder{get{return Color.FromArgb(63,72,65);}}
  public override Color MenuItemSelected{get{return Color.FromArgb(70,89,39);}}
  public override Color MenuItemBorder{get{return Brand.Lime;}}
  public override Color SeparatorDark{get{return Color.FromArgb(63,72,65);}}
  public override Color SeparatorLight{get{return Brand.Panel;}}
 }
}
namespace Captura {
 static class Startup {
  const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
  public static bool Enabled{get{using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath)){return key!=null&&!string.IsNullOrEmpty(key.GetValue("Priint") as string);}}}
  public static string Command(string path){return "\""+path+"\" --background";}
  public static void Set(bool enabled){using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyPath)){if(enabled)key.SetValue("Priint",Command(Application.ExecutablePath),Microsoft.Win32.RegistryValueKind.String);else key.DeleteValue("Priint",false);}}
 }
}


