using System;using System.Collections.Generic;using System.Threading;
namespace Captura {
 sealed class FrameExchange {
  public sealed class Frame:IDisposable {internal FrameExchange Owner;public byte[] Bytes;internal int References;public void Dispose(){lock(Owner.sync){References--;if(References==0)Owner.pool.Push(this);}}}
  readonly object sync=new object();readonly Stack<Frame> pool=new Stack<Frame>();Frame latest;readonly int size;
  public FrameExchange(int length){size=length;for(int i=0;i<3;i++)pool.Push(new Frame{Owner=this,Bytes=new byte[length]});}
  public void Publish(byte[] source){Frame frame;lock(sync){frame=pool.Count>0?pool.Pop():new Frame{Owner=this,Bytes=new byte[size]};frame.References=1;}Buffer.BlockCopy(source,0,frame.Bytes,0,size);Frame old;lock(sync){old=latest;latest=frame;}if(old!=null)old.Dispose();}
  public Frame Acquire(){lock(sync){if(latest==null)return null;latest.References++;return latest;}}
 }
}
