using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace Captura {
 sealed class RecordingHud:Form {
  [DllImport("user32.dll")]static extern bool SetWindowDisplayAffinity(IntPtr h,uint affinity);Timer blink=new Timer{Interval=500};string text="";Color color=Brand.Lime;bool light=true,cancelled;public bool Excluded{get;private set;}
  public RecordingHud(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;Opacity=.99;BackColor=Color.FromArgb(21,24,26);ClientSize=new Size(240,38);StartPosition=FormStartPosition.Manual;var work=Screen.PrimaryScreen.WorkingArea;Location=new Point(work.Right-260,work.Top+18);blink.Tick+=delegate{light=text=="Gravando"?!light:true;Invalidate();};blink.Start();}
  protected override bool ShowWithoutActivation{get{return true;}}
  protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
  protected override void OnShown(EventArgs e){base.OnShown(e);Excluded=SetWindowDisplayAffinity(Handle,0x11);}
  public void Cancel(){cancelled=true;}
  public async System.Threading.Tasks.Task<bool> Countdown(){text="3";Show();for(int i=3;i>0;i--){if(cancelled)throw new OperationCanceledException("Contagem cancelada.");text="Começa em "+i;color=Brand.Lime;Invalidate();await System.Threading.Tasks.Task.Delay(1000);}if(cancelled)throw new OperationCanceledException("Contagem cancelada.");if(!Excluded){Hide();return false;}text="Preparando…";Invalidate();return true;}
  public void State(string state){text=state;color=state=="Pausado"?Color.Gold:state=="Parado"?Color.OrangeRed:Brand.Lime;light=true;if(state=="Parado"||state=="Gravação concluída"||Excluded)Show();else Hide();Invalidate();}
  protected override void OnPaint(PaintEventArgs e){using(var b=new SolidBrush(light?color:Color.FromArgb(60,color)))e.Graphics.FillEllipse(b,12,11,15,15);TextRenderer.DrawText(e.Graphics,text,SystemFonts.MessageBoxFont,new Rectangle(36,8,200,24),Color.White,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);}
  protected override void Dispose(bool d){if(d)blink.Dispose();base.Dispose(d);}
 }
 class AvOptionsForm:Form {
  public Preferences Result;EncodingSettingsControl encoding;NumericUpDown width,height,zoom;DarkChoice shape,audioMode,output,input,captureInput;CheckBox mirror,track,screen;
  public AvOptionsForm(Preferences p){Result=p.Clone();Text="Priint · Configurações de gravação";Icon=Brand.AppIcon();ClientSize=new Size(720,650);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;BackColor=Brand.Back;ForeColor=Brand.Ink;Font=new Font("Segoe UI",10);
   var tabs=new SectionTabs{Location=new Point(20,20),Size=new Size(680,540)};Controls.Add(tabs);var video=tabs.AddPage("Vídeo");var cam=tabs.AddPage("Câmera");var audio=tabs.AddPage("Áudio");var access=tabs.AddPage("Acesso");
   encoding=new EncodingSettingsControl(p){Location=new Point(10,14)};video.Controls.Add(encoding);
   Label(cam,"Enquadramento da câmera",20,20);Label(cam,"Largura (px)",20,74);width=Number(cam,p.CameraWidth,64,3840,210,70);Label(cam,"Altura (px)",20,122);height=Number(cam,p.CameraHeight,64,2160,210,118);
   shape=Choice(cam,20,174,300,new object[]{"Retangular","Quadrada","Redonda"},p.CameraShape);Label(cam,"Zoom (%)",20,228);zoom=Number(cam,(int)(p.CameraZoom*100),100,400,210,224);
   mirror=Check(cam,"Espelhar imagem",p.CameraMirror,20,280);track=Check(cam,"Enquadramento automático · seguir rosto",p.CameraTracking,20,322);track.CheckedChanged+=delegate{if(track.Checked&&zoom.Value<140)zoom.Value=140;};Label(cam,"O rastreio é local. Use zoom acima de 100% para reenquadrar.",20,368);
   Label(cam,"Entrada da câmera",20,408);captureInput=Choice(cam,210,404,430,new object[]{"Automático","720p / 30","1080p / 30","Dispositivo padrão"},p.CameraInput);screen=Check(cam,"Prévia ao vivo da tela selecionada",p.LivePreview,20,458);
   Label(audio,"O que gravar?",20,22);audioMode=Choice(audio,20,56,620,new object[]{"Sem áudio","Só áudio do computador","Só microfone","Computador + microfone"},new object[]{"Sem áudio","Só áudio do computador","Só microfone","Computador + microfone"}[p.AudioMode]);
   Label(audio,"Saída / fone a capturar",20,120);output=Choice(audio,20,154,620,new object[0],null);Label(audio,"Microfone",20,222);input=Choice(audio,20,256,620,new object[0],null);
   try{foreach(var d in AudioDevices.List(true))output.Items.Add(d);foreach(var d in AudioDevices.List(false))input.Items.Add(d);SelectDevice(output,p.OutputDevice);SelectDevice(input,p.MicrophoneDevice);}catch(Exception ex){Label(audio,"Não foi possível listar áudio: "+ex.Message,20,315);}
   Label(audio,"MP3 separado: habilite na aba Vídeo. O áudio também fica no vídeo.",20,380);
   Label(access,"Câmera e microfone são usados somente quando você os habilita.",20,24);
   Label(access,"Se o Windows bloquear o acesso, habilite os aplicativos da área\nde trabalho nas configurações de privacidade abaixo.",20,74);
   var camAccess=Brand.Button("Permissões da câmera");camAccess.SetBounds(20,160,300,40);camAccess.Click+=delegate{DeviceAccess.Open(true);};access.Controls.Add(camAccess);
   var micAccess=Brand.Button("Permissões do microfone");micAccess.SetBounds(20,220,300,40);micAccess.Click+=delegate{DeviceAccess.Open(false);};access.Controls.Add(micAccess);
   Label(access,"Nenhum dispositivo é ativado ao instalar o Priint.\nCâmeras virtuais também dependem do aplicativo de origem.",20,300);
   var status=new Label{Location=new Point(20,566),Size=new Size(390,70),ForeColor=Color.Orange};Controls.Add(status);
   var ok=Brand.Button("Aplicar");ok.Location=new Point(560,584);ok.Click+=delegate{try{var next=Result.Clone();encoding.Read(next);next.CameraWidth=(int)width.Value;next.CameraHeight=(int)height.Value;next.CameraShape=shape.SelectedItem.ToString();next.CameraZoom=(float)zoom.Value/100;next.CameraMirror=mirror.Checked;next.CameraTracking=track.Checked;next.AudioMode=audioMode.SelectedIndex;next.OutputDevice=output.SelectedItem is AudioDeviceChoice?((AudioDeviceChoice)output.SelectedItem).Id:"";next.MicrophoneDevice=input.SelectedItem is AudioDeviceChoice?((AudioDeviceChoice)input.SelectedItem).Id:"";next.LivePreview=screen.Checked;next.CameraInput=captureInput.SelectedItem==null?"Automático":captureInput.SelectedItem.ToString();Result=next;DialogResult=DialogResult.OK;Close();}catch(Exception ex){status.Text=ex.Message;}};Controls.Add(ok);
   var cancel=Brand.Button("Cancelar");cancel.Location=new Point(414,584);cancel.DialogResult=DialogResult.Cancel;Controls.Add(cancel);CancelButton=cancel;
  }
  void Label(Control parent,string text,int x,int y){parent.Controls.Add(new Label{Text=text,Location=new Point(x,y),AutoSize=true,ForeColor=Brand.Ink});}
  CheckBox Check(Control parent,string text,bool value,int x,int y){var c=new CheckBox{Text=text,Checked=value,Location=new Point(x,y),AutoSize=true};parent.Controls.Add(c);return c;}
  NumericUpDown Number(Control parent,int value,int min,int max,int x,int y){var n=new NumericUpDown{Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),Location=new Point(x,y),Width=130,BackColor=Brand.Back,ForeColor=Brand.Ink};parent.Controls.Add(n);return n;}
  DarkChoice Choice(Control parent,int x,int y,int width,object[] values,object selected){var c=new DarkChoice{Location=new Point(x,y),Width=width};c.Items.AddRange(values);if(values.Length>0)c.SelectedItem=selected;parent.Controls.Add(c);return c;}
  static void SelectDevice(DarkChoice c,string id){for(int i=0;i<c.Items.Count;i++)if(((AudioDeviceChoice)c.Items[i]).Id==id){c.SelectedIndex=i;return;}if(c.Items.Count>0)c.SelectedIndex=0;}
 }
 static class DeviceAccess {
  public static void Check(bool camera){
   string capability=camera?"webcam":"microphone";
   string path=@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\"+capability;
   foreach(var root in new[]{Microsoft.Win32.Registry.CurrentUser,Microsoft.Win32.Registry.LocalMachine})
    foreach(string entry in new[]{path,path+@"\NonPackaged"})
     using(var key=root.OpenSubKey(entry)){
      if(key!=null&&string.Equals(key.GetValue("Value") as string,"Deny",StringComparison.OrdinalIgnoreCase))
       throw new InvalidOperationException("O Windows bloqueou "+(camera?"a câmera":"o microfone")+". Abra Configurações de gravação > Acesso para permitir aplicativos da área de trabalho.");
     }
  }
  public static void Open(bool camera){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(camera?"ms-settings:privacy-webcam":"ms-settings:privacy-microphone"){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message,"Priint");}}
 }
}



