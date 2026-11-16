using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace Captura {
    // One CPU buffer is shared by capture, annotation and the encoder pipe.
    sealed class PinnedCanvas : IDisposable {
        public readonly byte[] Bytes;
        public readonly IntPtr Pointer;
        public readonly Bitmap Image;
        GCHandle pinned;
        public PinnedCanvas(int width, int height) {
            Bytes = new byte[checked(width * height * 4)];
            pinned = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
            Pointer = pinned.AddrOfPinnedObject();
            try { Image = new Bitmap(width, height, width * 4, PixelFormat.Format32bppArgb, Pointer); }
            catch { pinned.Free(); throw; }
        }
        public void Dispose() { Image.Dispose(); if (pinned.IsAllocated) pinned.Free(); }
    }

    // All D3D calls stay on the capture thread. Keep the last staging texture
    // when the desktop is unchanged, while camera composition keeps updating.
    sealed class DesktopFrames : IDisposable {
        sealed class Display : IDisposable {
            public Rectangle Bounds;
            public Device Device;
            public Output1 Output;
            public OutputDuplication Duplication;
            public Texture2D Staging;
            public bool Ready;
            public void Dispose() {
                if (Duplication != null) Duplication.Dispose();
                if (Staging != null) Staging.Dispose();
                if (Output != null) Output.Dispose();
                if (Device != null) Device.Dispose();
            }
        }
        readonly List<Display> displays = new List<Display>();
        public DesktopFrames() {
            try {
                using (var factory = new Factory1()) {
                    foreach (var adapter in factory.Adapters1) using (adapter) {
                        foreach (var output in adapter.Outputs) using (output) {
                            var desc = output.Description;
                            if (!desc.IsAttachedToDesktop) continue;
                            if (desc.Rotation != DisplayModeRotation.Identity && desc.Rotation != DisplayModeRotation.Unspecified)
                                throw new NotSupportedException("Monitor rotacionado: captura compatível.");
                            var display = new Display();
                            try {
                                display.Bounds = Rectangle.FromLTRB(desc.DesktopBounds.Left, desc.DesktopBounds.Top, desc.DesktopBounds.Right, desc.DesktopBounds.Bottom);
                                display.Device = new Device(adapter, DeviceCreationFlags.BgraSupport);
                                display.Output = output.QueryInterface<Output1>();
                                display.Duplication = display.Output.DuplicateOutput(display.Device);
                                display.Staging = new Texture2D(display.Device, new Texture2DDescription {
                                    Width = display.Bounds.Width, Height = display.Bounds.Height,
                                    MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                                    SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging,
                                    CpuAccessFlags = CpuAccessFlags.Read, BindFlags = BindFlags.None, OptionFlags = ResourceOptionFlags.None
                                });
                                displays.Add(display);
                            } catch { display.Dispose(); throw; }
                        }
                    }
                }
                if (displays.Count == 0) throw new InvalidOperationException("Nenhum monitor disponível para DXGI.");
            } catch { Dispose(); throw; }
        }
        public void CopyTo(IntPtr target, int stride, Rectangle area, Rectangle clip) {
            foreach (var display in displays) {
                Rectangle visible = Rectangle.Intersect(clip, display.Bounds);
                if (visible.IsEmpty) continue;
                SharpDX.DXGI.Resource resource = null;
                OutputDuplicateFrameInformation info;
                var result = display.Duplication.TryAcquireNextFrame(display.Ready ? 0 : 500, out info, out resource);
                if (result.Success) {
                    try {
                        using (var texture = resource.QueryInterface<Texture2D>()) {
                            display.Device.ImmediateContext.CopyResource(texture, display.Staging);
                        }
                        display.Ready = true;
                    } finally {
                        if (resource != null) resource.Dispose();
                        display.Duplication.ReleaseFrame();
                    }
                } else {
                    if (resource != null) resource.Dispose();
                    if (result != SharpDX.DXGI.ResultCode.WaitTimeout) result.CheckError();
                    if (!display.Ready) throw new InvalidOperationException("O monitor não forneceu seu primeiro quadro.");
                }
                var context = display.Device.ImmediateContext;
                var data = context.MapSubresource(display.Staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                try {
                    int sourceX = visible.X - display.Bounds.X, sourceY = visible.Y - display.Bounds.Y;
                    int destX = visible.X - area.X, destY = visible.Y - area.Y;
                    for (int row = 0; row < visible.Height; row++)
                        CopyMemory(IntPtr.Add(target, (destY + row) * stride + destX * 4),
                            IntPtr.Add(data.DataPointer, (sourceY + row) * data.RowPitch + sourceX * 4), (UIntPtr)(visible.Width * 4));
                } finally { context.UnmapSubresource(display.Staging, 0); }
            }
        }
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        static extern void CopyMemory(IntPtr target, IntPtr source, UIntPtr bytes);
        public void Dispose() { foreach (var display in displays) display.Dispose(); displays.Clear(); }
    }
}

