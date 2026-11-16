using System;using System.IO;using System.Drawing;using System.Diagnostics;using System.Windows.Forms;
namespace Captura {
 class RecordingLibraryForm:Form {
  ListView list;string folder;
  public RecordingLibraryForm(string path,string selected){folder=path;Text="Priint · Gravações";Icon=Brand.AppIcon();ClientSize=new Size(800,480);StartPosition=FormStartPosition.CenterParent;BackColor=Brand.Back;ForeColor=Brand.Ink;Font=new Font("Segoe UI",10);
   Controls.Add(new Label{Text=selected==null?"Suas gravações":"Gravação concluída",Location=new Point(24,20),AutoSize=true,ForeColor=Brand.Lime,Font=new Font("Segoe UI",18,FontStyle.Bold)});
   list=new ListView{Location=new Point(24,68),Size=new Size(752,330),View=View.Details,FullRowSelect=true,MultiSelect=false,BackColor=Brand.Panel,ForeColor=Brand.Ink,HideSelection=false};list.Columns.Add("Gravação",440);list.Columns.Add("Data",170);list.Columns.Add("Tamanho",110);Controls.Add(list);
   if(Directory.Exists(folder)){var files=Array.FindAll(new DirectoryInfo(folder).GetFiles(),f=>!f.Name.Contains(".partial")&&(f.Extension.Equals(".mp4",StringComparison.OrdinalIgnoreCase)||f.Extension.Equals(".avi",StringComparison.OrdinalIgnoreCase)));Array.Sort(files,(a,b)=>b.LastWriteTime.CompareTo(a.LastWriteTime));foreach(var f in files){var item=new ListViewItem(new[]{f.Name,f.LastWriteTime.ToString("dd/MM HH:mm"),(f.Length/1048576d).ToString("F1")+" MB"}){Tag=f.FullName};list.Items.Add(item);if(f.FullName==selected)item.Selected=true;}}
   var play=Brand.Button("Reproduzir");play.SetBounds(24,420,150,38);play.Click+=delegate{Play();};Controls.Add(play);list.DoubleClick+=delegate{Play();};
   var open=Brand.Button("Abrir pasta");open.SetBounds(188,420,150,38);open.Click+=delegate{Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});};Controls.Add(open);
   var close=Brand.Button("Fechar");close.SetBounds(626,420,150,38);close.Click+=delegate{Close();};Controls.Add(close);
   Shown+=delegate{if(list.SelectedItems.Count>0)list.SelectedItems[0].EnsureVisible();};
  }
  void Play(){if(list.SelectedItems.Count==0)return;try{Process.Start(new ProcessStartInfo((string)list.SelectedItems[0].Tag){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message,"Priint");}}
 }
}
