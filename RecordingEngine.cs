using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Captura
{
    public enum RecordingMode
    {
        Region,
        Monitor,
        Window,
        Desktop
    }

    public class RecordingRequest
    {
        public RecordingMode Mode { get; set; }
        public Rectangle Region { get; set; }
        public IntPtr WindowHandle { get; set; }
        public string OutputDirectory { get; set; }
        public int Fps { get; set; }
        public bool CaptureCursor { get; set; }
        public bool FollowMouse { get; set; }
        public Size FollowSize { get; set; }
        public string EncoderPath { get; set; }
        public bool UseNvenc { get; set; } public int GpuIndex {get;set;} public string Codec {get;set;} public string Container {get;set;} public string Resolution {get;set;} public string Quality {get;set;} public int MinMbps {get;set;} public int MaxMbps {get;set;} public bool SeparateMp3 {get;set;}
        public string CameraDevice {get;set;}
        public Rectangle CameraBounds {get;set;}
        public bool TestCamera {get;set;}
        public string[] TestAudioTracks {get;set;} public CameraFeed Camera {get;set;} public string CameraShape {get;set;} public float CameraZoom {get;set;} public bool CameraMirror {get;set;} public bool CameraTracking {get;set;} public bool SystemAudio {get;set;} public bool Microphone {get;set;} public string OutputDeviceId {get;set;} public string MicrophoneId {get;set;}
    }

    public class RecordingEngine : IDisposable
    {
        private const int CursorShowing = 0x00000001;
        private const int DiNormal = 0x00000003;
        private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeCursorInfo
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public NativePoint ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeIconInfo
        {
            public int fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorInfo(ref NativeCursorInfo pci);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetIconInfo(IntPtr hIcon, out NativeIconInfo piconinfo);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAni, IntPtr hbrFlickerFree, int diFlags);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint pt);

        private MediaClock clock;private AudioSession audio;private CameraFeed ownedCamera;private string[] audioFiles=new string[0];
        public double RecordedSeconds{get{return clock==null?0:clock.Seconds;}}private bool pauseRequested;public bool IsPaused {get{lock(_lock)return _isRecording&&pauseRequested;}} public void Pause(){lock(_lock){if(!_isRecording)return;pauseRequested=true;if(clock!=null)clock.Pause();}}public void Resume(){lock(_lock){if(!_isRecording)return;pauseRequested=false;if(clock!=null)clock.Resume();}}
        private readonly object _lock = new object();
        private SynchronizationContext _ui;
        private bool _isRecording;
        private bool _disposed;
        private bool _failureNotified;
        private bool _processExited;
        private int _processExitCode;
        private RecordingRequest _request;
        private Rectangle _captureRect;
        private Rectangle _fixedViewport;private Size _outputSize;private TaskCompletionSource<bool> _ready;public Task Ready{get{return _ready==null?Task.FromResult(true):_ready.Task;}}public double EffectiveFps{get;private set;}public bool PerformanceLimited{get;private set;}
        private Process _ffmpeg;
        private Stream _stdin;
        private Thread _captureThread;
        private TaskCompletionSource<string> _stopTcs;
        private CancellationTokenSource _cts;
        private string _partialPath;
        private string _finalPath;
        private string _outputPath;
        private StringBuilder _stderr;

        public bool IsRecording
        {
            get { lock (_lock) return _isRecording; }
        }

        public string OutputPath
        {
            get { lock (_lock) return _outputPath; }
        }

        public event Action<string> Status;
        public event Action<string> Failed;

        public void Start(RecordingRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (string.IsNullOrEmpty(request.EncoderPath)) throw new ArgumentException("EncoderPath não informado", "request");
            if (!File.Exists(request.EncoderPath)) throw new FileNotFoundException("Executável do ffmpeg não encontrado", request.EncoderPath);

            if(request.Fps<=0)request.Fps=30;VideoProfiles.Validate(request);RecordingRequest snap;
            lock (_lock)
            {
                if (_isRecording) throw new InvalidOperationException("Gravação já em andamento");
                if (_disposed) throw new ObjectDisposedException("RecordingEngine");
                snap = new RecordingRequest
                {
                    Mode = request.Mode,
                    Region = request.Region,
                    WindowHandle = request.WindowHandle,
                    OutputDirectory = request.OutputDirectory,
                    Fps = request.Fps,
                    CaptureCursor = request.CaptureCursor,
                    FollowMouse = request.FollowMouse,
                    FollowSize = request.FollowSize,
                    EncoderPath = request.EncoderPath,
                    UseNvenc = request.UseNvenc, GpuIndex=request.GpuIndex,Codec=request.Codec??"H.264",Container=request.Container??"MP4",Resolution=request.Resolution??"Original",Quality=request.Quality??"Equilibrada",MinMbps=request.MinMbps,MaxMbps=request.MaxMbps,SeparateMp3=request.SeparateMp3
                    ,CameraDevice=request.CameraDevice,CameraBounds=request.CameraBounds,TestCamera=request.TestCamera,Camera=request.Camera,CameraShape=request.CameraShape,CameraZoom=request.CameraZoom,CameraMirror=request.CameraMirror,CameraTracking=request.CameraTracking,SystemAudio=request.SystemAudio,Microphone=request.Microphone,OutputDeviceId=request.OutputDeviceId,MicrophoneId=request.MicrophoneId,TestAudioTracks=request.TestAudioTracks
                };
                _ui = SynchronizationContext.Current;
                _isRecording = true;pauseRequested=false;_ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);clock=null;audio=null;ownedCamera=null;EffectiveFps=0;PerformanceLimited=false;
                _failureNotified = false;
                _processExited = false;
                _processExitCode = 0;
                _stderr = new StringBuilder();
                _cts = new CancellationTokenSource();
                _stopTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _request = snap;
                _outputPath = null;pendingMp3=null;finalMp3=null;
            }

            try
            {
                Rectangle desktop = SystemInformation.VirtualScreen;
                NativePoint cursor;
                GetCursorPos(out cursor);
                NativeRect wr;
                Rectangle windowRect = GetWindowRect(request.WindowHandle, out wr) ? new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top) : Rectangle.Empty;

                _captureRect = ComputeArea(snap, desktop, new Point(cursor.X, cursor.Y), windowRect);
                if (_captureRect.Width <= 0 || _captureRect.Height <= 0)
                    throw new InvalidOperationException("Área de captura inválida");
                _fixedViewport = _captureRect;_outputSize=VideoProfiles.OutputSize(snap.Resolution,_captureRect.Size); string probeText;int probeCode;if(!RunProcess(snap.EncoderPath,"-v error -f lavfi -i color=size="+_outputSize.Width+"x"+_outputSize.Height+":rate="+snap.Fps+" -frames:v 1"+VideoProfiles.EncoderArguments(snap)+" -f null -",TimeSpan.FromSeconds(15),out probeText,out probeCode)||probeCode!=0)throw new InvalidOperationException("Codificador selecionado indisponível: "+Limit(probeText,350));

                if (string.IsNullOrEmpty(snap.OutputDirectory)) throw new InvalidOperationException("OutputDirectory não informado");
                if (!Directory.Exists(snap.OutputDirectory)) Directory.CreateDirectory(snap.OutputDirectory);

                _partialPath = Path.Combine(snap.OutputDirectory, Guid.NewGuid().ToString() + ".partial."+snap.Container.ToLowerInvariant());
                _finalPath = Path.Combine(snap.OutputDirectory, "Priint-"+DateTime.Now.ToString("yyyy-MM-dd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+"."+snap.Container.ToLowerInvariant());

                if((snap.Camera!=null||snap.TestCamera||!string.IsNullOrEmpty(snap.CameraDevice))&&snap.Mode==RecordingMode.Region)throw new ArgumentException("Câmera apenas em tela ou janela.");
                if(snap.Camera==null&&(snap.TestCamera||!string.IsNullOrEmpty(snap.CameraDevice))){ownedCamera=new CameraFeed();ownedCamera.Start(snap.EncoderPath,snap.CameraDevice,snap.TestCamera);snap.Camera=ownedCamera;}
                string args = BuildArguments(snap, _captureRect.Width, _captureRect.Height, _partialPath);
                var psi = new ProcessStartInfo
                {
                    FileName = snap.EncoderPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true
                };
                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.ErrorDataReceived += OnErrorData;
                proc.Exited += OnProcessExited;
                proc.Start();
                Stream stdin = proc.StandardInput.BaseStream;
                lock (_lock)
                {
                    _ffmpeg = proc;
                    _stdin = stdin;
                }
                proc.BeginErrorReadLine();

                int fps = snap.Fps > 0 ? snap.Fps : 30;
                NotifyStatus(String.Format("Gravação iniciada (alvo {0} fps).", fps));
                clock=new MediaClock();clock.Pause();audioFiles=snap.TestAudioTracks??new string[0];if(snap.SystemAudio||snap.Microphone){audio=new AudioSession(clock);audio.Start(_partialPath,snap.SystemAudio,snap.Microphone,snap.OutputDeviceId,snap.MicrophoneId);}
                _captureThread = new Thread(new ThreadStart(CaptureLoop)) { IsBackground = true };
                _captureThread.Start();
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _isRecording = false;
                    if (_ffmpeg != null) { try { if(!_ffmpeg.HasExited)_ffmpeg.Kill(); _ffmpeg.Dispose(); } catch { } _ffmpeg = null; }
                    _stdin = null;
                    if (_cts != null) { _cts.Dispose(); _cts = null; }
                    if (_stopTcs != null) { _stopTcs.TrySetResult(null); _stopTcs = null; }
                }
                if(audio!=null){audio.Dispose();audio=null;}if(ownedCamera!=null){ownedCamera.Dispose();ownedCamera=null;}
                if(_ready!=null)_ready.TrySetException(ex);Fail(Limit(ex.Message, 200));
                throw;
            }
        }

        public Task<string> StopAsync()
        {
            string currentPath;
            TaskCompletionSource<string> tcs;
            lock (_lock)
            {
                currentPath = _outputPath;
                if (!_isRecording) return Task.FromResult<string>(currentPath);
                if(clock!=null)clock.Finish();_cts.Cancel();
                tcs = _stopTcs;
            }
            Task.Run(async delegate { await Task.Delay(StopWaitTimeout).ConfigureAwait(false); lock(_lock){if(_stopTcs==tcs && _ffmpeg!=null){try{if(!_ffmpeg.HasExited)_ffmpeg.Kill();}catch{}}} });
            return tcs.Task;
        }

        public void Dispose()
        {
            lock (_lock) { _disposed = true; }
            if (IsRecording)
            {
                try { StopAsync().Wait((int)(StopWaitTimeout.TotalMilliseconds + DecodeTimeout.TotalMilliseconds)); }
                catch { }
            }
            lock (_lock)
            {
                if (_isRecording)
                {
                    try
                    {
                        Process p = _ffmpeg;
                        if (p != null && !p.HasExited) { try { p.Kill(); } catch { } }
                    }
                    catch { }
                    _isRecording = false;
                    _ffmpeg = null;
                    _stdin = null;
                    if (_cts != null) { _cts.Dispose(); _cts = null; }
                    if (_stopTcs != null) { _stopTcs.TrySetResult(null); _stopTcs = null; }
                }
            }
            GC.SuppressFinalize(this);
        }

        public static Rectangle ComputeArea(RecordingRequest request, Rectangle desktop, Point cursor, Rectangle windowRect)
        {
            if (request == null) throw new ArgumentNullException("request");

            Rectangle area;
            switch (request.Mode)
            {
                case RecordingMode.Window:
                    area = windowRect;
                    break;
                case RecordingMode.Monitor:
                    area = (request.Region.Width > 0 && request.Region.Height > 0 && desktop.Contains(request.Region)) ? request.Region : desktop;
                    break;
                case RecordingMode.Region:
                    area = request.Region;
                    break;
                case RecordingMode.Desktop:
                default:
                    area = desktop;
                    break;
            }

            if (request.FollowMouse && request.FollowSize.Width > 0 && request.FollowSize.Height > 0)
            {
                int w = request.FollowSize.Width;
                int h = request.FollowSize.Height;
                area = new Rectangle(cursor.X - w / 2, cursor.Y - h / 2, w, h);
            }

            if(area.Width<2||area.Height<2||desktop.Width<2||desktop.Height<2)throw new ArgumentException("Área vazia ou janela indisponível.");
            int width = Math.Min(desktop.Width,area.Width);
            int height = Math.Min(desktop.Height,area.Height);
            width -= width % 2;
            height -= height % 2;

            int right = desktop.X + desktop.Width;
            int bottom = desktop.Y + desktop.Height;
            int x = Math.Max(desktop.X, Math.Min(area.X, right - width));
            int y = Math.Max(desktop.Y, Math.Min(area.Y, bottom - height));

            return new Rectangle(x, y, width, height);
        }

        public static string BuildArguments(RecordingRequest r, int width, int height, string output)
        {
            var sb = new StringBuilder();
            sb.Append("-hide_banner -loglevel warning -f rawvideo -pixel_format bgra -video_size ");
            sb.Append(width);
            sb.Append('x');
            sb.Append(height);
            sb.Append(" -framerate ");
            sb.Append(r.Fps > 0 ? r.Fps : 30);
            sb.Append(" -i pipe:0");
            sb.Append(" -an -fps_mode cfr -r ");sb.Append(r.Fps>0?r.Fps:30);
            Size scaled=VideoProfiles.OutputSize(r.Resolution,new Size(width,height));
            if(scaled.Width!=width||scaled.Height!=height)sb.Append(" -vf scale="+scaled.Width+":"+scaled.Height+":flags=fast_bilinear");
            sb.Append(VideoProfiles.EncoderArguments(r));
            sb.Append(" -bf 0 -pix_fmt yuv420p ");
            if(r.Container!="AVI")sb.Append(" -movflags +faststart ");
            sb.Append(Quote(output));
            return sb.ToString();
        }

        public string PerformanceSummary{get;private set;} public string CaptureBackend{get;private set;}
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWaitableTimerEx(IntPtr attributes,string name,uint flags,uint access);
        [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr arg,bool resume);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        private void CaptureLoop()
        {
            string error = null;Thread writer=null;string writerError=null;
            int framesWritten = 0;int captured=0;long screenCost=0,cameraCost=0,copyCost=0,writeCost=0;var elapsed=Stopwatch.StartNew();long stage=0;DesktopFrames accelerated=null;IntPtr frameTimer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);bool fineTimer=frameTimer==IntPtr.Zero&&timeBeginPeriod(1)==0;
            try
            {
                RecordingRequest req;
                lock (_lock) { req = _request; }

                int fps = req.Fps > 0 ? req.Fps : 30;
                Rectangle desktop = SystemInformation.VirtualScreen;

                try{accelerated=new DesktopFrames();CaptureBackend="DXGI";}catch(Exception ex){CaptureBackend="GDI: "+Limit(ex.Message,150);}
                using (var surface = new PinnedCanvas(_captureRect.Width, _captureRect.Height))
                {
                    Bitmap canvas=surface.Image;byte[] buffer = surface.Bytes;
                    int rowBytes = canvas.Width * 4;var exchange=new FrameExchange(buffer.Length);double nextCapture=0;
                    if(accelerated!=null){try{accelerated.CopyTo(surface.Pointer,rowBytes,_captureRect,_captureRect);}catch(Exception ex){accelerated.Dispose();accelerated=null;CaptureBackend="GDI: "+Limit(ex.Message,150);}}
                    lock(_lock){if(!pauseRequested)clock.Resume();}elapsed.Restart();
                    writer=new Thread(delegate(){
                     IntPtr pacing=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);
                     try{
                      while(!_cts.IsCancellationRequested){
                       if(clock.Paused){Thread.Sleep(10);continue;}
                       int expected=(int)Math.Floor(clock.Seconds*fps)+1;
                       if(framesWritten>=expected){long due=-10000L;if(pacing!=IntPtr.Zero&&SetWaitableTimer(pacing,ref due,0,IntPtr.Zero,IntPtr.Zero,false))WaitForSingleObject(pacing,50);else Thread.Sleep(1);continue;}
                       using(var frame=exchange.Acquire()){if(frame==null){Thread.Sleep(1);continue;}long before=Stopwatch.GetTimestamp();WriteFrame(frame.Bytes);Interlocked.Add(ref writeCost,Stopwatch.GetTimestamp()-before);framesWritten++;if(framesWritten==1)_ready.TrySetResult(true);}
                      }
                      if(writerError==null&&framesWritten>0){int target=(int)Math.Round(clock.Seconds*fps);while(framesWritten<target){using(var frame=exchange.Acquire()){WriteFrame(frame.Bytes);framesWritten++;}}}
                     }catch(Exception ex){writerError=ex.Message;clock.Finish();_cts.Cancel();}finally{if(pacing!=IntPtr.Zero)CloseHandle(pacing);}
                    }){IsBackground=true,Name="Priint encoder"};writer.Start();

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        if(audio!=null)audio.CheckHealth();if(clock.Paused){Thread.Sleep(25);continue;}
                        lock (_lock)
                        {
                            if (_processExited && framesWritten == 0)
                            {
                                error = "Codificador encerrou antes do primeiro frame";
                                break;
                            }
                        }

                        Rectangle area;
                        if (req.Mode == RecordingMode.Window)
                        {
                            if (!IsWindow(req.WindowHandle) || IsIconic(req.WindowHandle))
                            {
                                error = "Janela não está visível ou foi destruída";
                                break;
                            }
                            NativeRect wr;
                            Rectangle cur = GetWindowRect(req.WindowHandle, out wr) ? new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top) : Rectangle.Empty;
                            if (cur.Width <= 0 || cur.Height <= 0)
                            {
                                error = "Janela não está visível";
                                break;
                            }
                            area = new Rectangle(cur.X, cur.Y, _fixedViewport.Width, _fixedViewport.Height);
                            area = ClampToDesktop(area, desktop);
                        }
                        else if (req.FollowMouse)
                        {
                            NativePoint cp;
                            GetCursorPos(out cp);
                            area = FollowArea(_fixedViewport.Size, new Point(cp.X, cp.Y), desktop);
                        }
                        else
                        {
                            area = _captureRect;
                        }

                                                Rectangle visibleArea=area;
                        if(req.Mode==RecordingMode.Window){NativeRect window;GetWindowRect(req.WindowHandle,out window);visibleArea=Rectangle.Intersect(area,Rectangle.Intersect(desktop,Rectangle.FromLTRB(window.Left,window.Top,window.Right,window.Bottom)));}
                        stage=Stopwatch.GetTimestamp();
                        if(accelerated!=null){try{Array.Clear(buffer,0,buffer.Length);accelerated.CopyTo(surface.Pointer,rowBytes,area,visibleArea);}catch(Exception ex){accelerated.Dispose();accelerated=null;CaptureBackend="GDI: "+Limit(ex.Message,150);}}
                        using (Graphics g = Graphics.FromImage(canvas))
                        {
                            if(accelerated==null){g.Clear(Color.Black);if(visibleArea.Width>0&&visibleArea.Height>0)g.CopyFromScreen(visibleArea.Location,new Point(visibleArea.X-area.X,visibleArea.Y-area.Y),visibleArea.Size);}
                            if(req.CaptureCursor)DrawCursor(g,area);
                            screenCost+=Stopwatch.GetTimestamp()-stage;stage=Stopwatch.GetTimestamp();
                            if(req.Camera!=null){if(!req.Camera.IsReady)throw new InvalidOperationException("A câmera parou de fornecer imagem. "+req.Camera.Error);req.Camera.DrawLatest(g,req.CameraBounds,req.CameraShape,req.CameraZoom<=0?1:req.CameraZoom,req.CameraMirror,req.CameraTracking);}
                        }
                        cameraCost+=Stopwatch.GetTimestamp()-stage;stage=Stopwatch.GetTimestamp();
                        long publishAt=Stopwatch.GetTimestamp();exchange.Publish(buffer);copyCost+=Stopwatch.GetTimestamp()-publishAt;captured++;
                        nextCapture=Math.Max(nextCapture+1d/fps,clock.Seconds);
                        int delay=(int)Math.Max(0,Math.Min(50,(nextCapture-clock.Seconds)*1000));
                        if(delay>0){long due=-delay*10000L;if(frameTimer!=IntPtr.Zero&&SetWaitableTimer(frameTimer,ref due,0,IntPtr.Zero,IntPtr.Zero,false))WaitForSingleObject(frameTimer,100);else Thread.Sleep(delay);}
                    }
                    if(clock!=null)clock.Finish();
                    if(writer!=null)WaitWriter(writer);
                    if(writerError!=null)error=writerError;

                }
            }
            catch (OperationCanceledException) { }
            catch (ThreadAbortException) { Thread.ResetAbort(); }
            catch (Exception ex) { if (error == null) error = Limit(ex.Message, 200); }
            finally
            {
                if(clock!=null)clock.Finish();if(_cts!=null)_cts.Cancel();if(writer!=null&&writer.IsAlive){try{WaitWriter(writer);}catch(Exception waitError){if(error==null)error=waitError.Message;}}if(error==null&&writerError!=null)error=writerError;
                try
                {
                    Stream s;
                    lock (_lock) { s = _stdin; }
                    if (s != null) s.Close();
                }
                catch { }
                if(_ready!=null&&framesWritten==0)_ready.TrySetException(new InvalidOperationException(error??"Gravação encerrada antes do primeiro quadro."));EffectiveFps=captured/Math.Max(.001,clock==null?elapsed.Elapsed.TotalSeconds:clock.Seconds);PerformanceLimited=framesWritten>0&&captured<framesWritten*.9;if(frameTimer!=IntPtr.Zero)CloseHandle(frameTimer);if(fineTimer)timeEndPeriod(1);if(accelerated!=null)accelerated.Dispose();PerformanceSummary=CaptureBackend+"; "+String.Format(CultureInfo.InvariantCulture,"captured={0}; written={1}; elapsed={2:F2}s; screen={3:F2}ms; camera={4:F2}ms; copy={5:F2}ms; pipe={6:F2}ms",captured,framesWritten,elapsed.Elapsed.TotalSeconds,screenCost*1000d/Stopwatch.Frequency/Math.Max(1,captured),cameraCost*1000d/Stopwatch.Frequency/Math.Max(1,captured),copyCost*1000d/Stopwatch.Frequency/Math.Max(1,captured),writeCost*1000d/Stopwatch.Frequency/Math.Max(1,captured));FinishRecording(error);
            }
        }

        private void WaitWriter(Thread writer){
            if(writer.Join(5000))return;
            lock(_lock){try{if(_ffmpeg!=null&&!_ffmpeg.HasExited)_ffmpeg.Kill();}catch{}}
            if(!writer.Join(3000))throw new InvalidOperationException("Codificador não respondeu ao encerramento.");
        }
        private void DrawCursor(Graphics g, Rectangle area)
        {
            NativeCursorInfo ci = new NativeCursorInfo();
            ci.cbSize = Marshal.SizeOf(typeof(NativeCursorInfo));
            if (!GetCursorInfo(ref ci)) return;
            if ((ci.flags & CursorShowing) == 0 || ci.hCursor == IntPtr.Zero) return;

            NativeIconInfo ii;
            if (!GetIconInfo(ci.hCursor, out ii)) return;
            try
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    int x = ci.ptScreenPos.X - area.X - ii.xHotspot;
                    int y = ci.ptScreenPos.Y - area.Y - ii.yHotspot;
                    DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, DiNormal);
                }
                finally { g.ReleaseHdc(); }
            }
            finally
            {
                if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            }
        }

        private void WriteFrame(byte[] buffer)
        {
            Stream s;
            lock (_lock) { s = _stdin; }
            if (s == null) throw new InvalidOperationException("Codificador não disponível");
            s.Write(buffer, 0, buffer.Length);
        }

        private static Rectangle FollowArea(Size size, Point cursor, Rectangle desktop)
        {
            int x = cursor.X - size.Width / 2;
            int y = cursor.Y - size.Height / 2;
            return ClampToDesktop(new Rectangle(x, y, size.Width, size.Height), desktop);
        }

        private static Rectangle ClampToDesktop(Rectangle rect, Rectangle desktop)
        {
            int w = Math.Min(rect.Width, desktop.Width);
            int h = Math.Min(rect.Height, desktop.Height);
            int x = Math.Max(desktop.X, Math.Min(rect.X, desktop.X + desktop.Width - w));
            int y = Math.Max(desktop.Y, Math.Min(rect.Y, desktop.Y + desktop.Height - h));
            return new Rectangle(x, y, w, h);
        }

        private void FinishRecording(string error)
        {
            Process proc = null;
            lock (_lock) { proc = _ffmpeg; }

            if (proc != null)
            {
                try
                {
                    if (!proc.HasExited && !proc.WaitForExit((int)StopWaitTimeout.TotalMilliseconds))
                    {
                        try { proc.Kill(); }
                        catch { }
                    }
                }
                catch { }
                if (error == null)
                {
                    try
                    {
                        if (proc.ExitCode != 0) error = String.Format("Codificador encerrou com código {0}: {1}", proc.ExitCode, Limit(_stderr==null?"":_stderr.ToString(),500));
                    }
                    catch { error = "Codificador encerrou inesperadamente"; }
                }
                try { proc.Dispose(); }
                catch { }
            }

            if(clock!=null)clock.Finish();if(audio!=null){try{audioFiles=audio.Stop();}catch(Exception ex){error="Áudio: "+ex.Message;}finally{audio.Dispose();audio=null;}}if(ownedCamera!=null){ownedCamera.Dispose();ownedCamera=null;}
            string promoted = null;
            if (error == null)
            {
                if(audioFiles.Length>0 && !MuxAudio()) error="Não foi possível finalizar as faixas de áudio.";
                if(error!=null){} else if (!ValidatePartial())
                    error = "Falha na validação do vídeo gravado";
                else
                {
                    try
                    {
                        File.Move(_partialPath, _finalPath);if(pendingMp3!=null){try{File.Move(pendingMp3,finalMp3);}catch{File.Move(_finalPath,_partialPath);throw;}}
                        promoted = _finalPath;
                        try {
                            string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Priint", "Logs");
                            Directory.CreateDirectory(logs);
                            File.WriteAllText(Path.Combine(logs, Path.GetFileNameWithoutExtension(_finalPath) + ".performance.txt"), PerformanceSummary);
                        } catch { }
                    }
                    catch (Exception ex) { error = Limit(ex.Message, 200); }
                }
            }

            string finalError = error;
            string promotedPath = promoted;
            lock (_lock)
            {
                _isRecording = false;
                _outputPath = promotedPath;
                _ffmpeg = null;
                _stdin = null;
                if (_cts != null) { _cts.Dispose(); _cts = null; }
                var tcs = _stopTcs;
                _stopTcs = null;
                if (tcs != null) {if(finalError!=null)tcs.TrySetException(new InvalidOperationException(finalError));else tcs.TrySetResult(promotedPath);}
            }

            if (finalError != null) Fail(finalError);
            else if (promotedPath != null) NotifyStatus("Gravação finalizada.");
        }

        private bool MuxAudio(){
            string mixed=_partialPath+".mixed."+_request.Container.ToLowerInvariant();
            var args=new StringBuilder("-v error -i "+Quote(_partialPath));
            foreach(string track in audioFiles)args.Append(" -i "+Quote(track));
            string filter=audioFiles.Length==2?"[1:a]aresample=48000,apad[a1];[2:a]aresample=48000,apad[a2];[a1][a2]amix=inputs=2:duration=longest:normalize=0,alimiter=limit=0.95[a]":"[1:a]aresample=48000,apad[a]";
            args.Append(" -filter_complex "+Quote(filter)+" -map 0:v:0 -map "+Quote("[a]")+" -c:v copy "+(_request.Container=="AVI"?"-c:a libmp3lame -b:a 320k":"-c:a aac -b:a 192k")+" -t "+Math.Max(.001,clock.Seconds).ToString("F6",CultureInfo.InvariantCulture)+" "+(_request.Container=="AVI"?"":"-movflags +faststart ")+Quote(mixed));
            string text;int code;
            if(!RunProcess(_request.EncoderPath,args.ToString(),TimeSpan.FromSeconds(120),out text,out code)||code!=0)return false;
            if(_request.SeparateMp3){
                string mp3=Path.ChangeExtension(_finalPath,".mp3"),temp=mp3+".partial.mp3";
                var mp3Args=new StringBuilder("-v error");
                foreach(string track in audioFiles)mp3Args.Append(" -i "+Quote(track));
                string mp3Filter=audioFiles.Length==2?"[0:a]aresample=48000,apad[a1];[1:a]aresample=48000,apad[a2];[a1][a2]amix=inputs=2:duration=longest:normalize=0,alimiter=limit=0.95[a]":"[0:a]aresample=48000,apad[a]";
                mp3Args.Append(" -filter_complex "+Quote(mp3Filter)+" -map "+Quote("[a]")+" -c:a libmp3lame -b:a 320k -t "+Math.Max(.001,clock.Seconds).ToString("F6",CultureInfo.InvariantCulture)+" "+Quote(temp));
                if(!RunProcess(_request.EncoderPath,mp3Args.ToString(),TimeSpan.FromSeconds(120),out text,out code)||code!=0)return false;
                if(!RunProcess(_request.EncoderPath,"-v error -i "+Quote(temp)+" -f null -",DecodeTimeout,out text,out code)||code!=0)return false;
                // Publish sidecar only after the video is validated.
                pendingMp3=temp;finalMp3=mp3;
            }
            File.Delete(_partialPath);File.Move(mixed,_partialPath);
            foreach(string track in audioFiles)File.Delete(track);
            return true;
        }
        private string pendingMp3,finalMp3;
        private bool ValidatePartial()
        {
            string partialPath;
            string encoder;
            lock (_lock)
            {
                partialPath = _partialPath;
                encoder = _request == null ? null : _request.EncoderPath;
            }
            if (string.IsNullOrEmpty(partialPath) || !File.Exists(partialPath)) return false;

            string ffprobe = ProbePath(encoder);
            if (!string.IsNullOrEmpty(ffprobe) && File.Exists(ffprobe))
            {
                string args = "-v error -select_streams v:0 -show_entries stream=codec_name,width,height,nb_frames,avg_frame_rate -show_entries format=duration -of default=noprint_wrappers=1 " + Quote(partialPath);
                string output;
                int code;
                if (!RunProcess(ffprobe, args, ProbeTimeout, out output, out code) || code != 0) return false;
                if (!ParseProbe(output)) return false;
            }

            if (string.IsNullOrEmpty(encoder)) return false;
            string decArgs = "-v error -i " + Quote(partialPath) + " -t 8 -f null -";
            string decOut;
            int decCode;
            return RunProcess(encoder, decArgs, DecodeTimeout, out decOut, out decCode) && decCode == 0;
        }

        private static bool RunProcess(string file, string args, TimeSpan timeout, out string output, out int exitCode)
        {
            output = null;
            exitCode = -1;
            Process p = null;
            try
            {
                p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var err = new StringBuilder();
                p.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (err)
                        {
                            if (err.Length > 1500) err.Length = 1500;
                            err.AppendLine(e.Data);
                        }
                    }
                };
                p.Start();
                p.BeginErrorReadLine();
                var readOutput = p.StandardOutput.ReadToEndAsync();
                bool exited = p.WaitForExit((int)timeout.TotalMilliseconds);
                if (!exited)
                {
                    try { p.Kill(); }
                    catch { }
                    exited = p.WaitForExit(5000);
                }
                exitCode = exited ? p.ExitCode : -1;
                output=readOutput.GetAwaiter().GetResult();if(exited){p.WaitForExit();if(exitCode!=0)lock(err)output+=err.ToString();}
                return exited;
            }
            catch { return false; }
            finally { if (p != null) p.Dispose(); }
        }

        private static string ProbePath(string encoderPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(encoderPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string probe = Path.Combine(dir, "ffprobe.exe");
                    if (File.Exists(probe)) return probe;
                }
            }
            catch { }
            return null;
        }

        private bool ParseProbe(string output)
        {
            if (string.IsNullOrEmpty(output)) return false;
            int width = 0, height = 0, frames = -1;
            double duration = 0;string codec=null;double actualRate=0;
            foreach (string line in output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int idx = line.IndexOf('=');
                if (idx < 0) continue;
                string key = line.Substring(0, idx).Trim();
                string val = line.Substring(idx + 1).Trim();
                switch (key)
                {
                    case "codec_name": codec=val;break;case "avg_frame_rate": var fraction=val.Split('/');if(fraction.Length==2&&ParseDouble(fraction[1])>0)actualRate=ParseDouble(fraction[0])/ParseDouble(fraction[1]);break;case "width": width = ParseInt(val); break;
                    case "height": height = ParseInt(val); break;
                    case "nb_frames": frames = ParseInt(val); break;
                    case "duration": duration = ParseDouble(val); break;
                }
            }
            if (width != _outputSize.Width || height != _outputSize.Height) return false;
            if (duration <= 0) return false;if(codec!=(_request.Codec=="H.265"?"hevc":"h264"))return false;if(Math.Abs(actualRate-_request.Fps)>.1)return false;
            if (frames >= 0 && frames <= 0) return false;
            return true;
        }

        private static int ParseInt(string s)
        {
            int v;
            return Int32.TryParse(s, out v) ? v : -1;
        }

        private static double ParseDouble(string s)
        {
            double v;
            return Double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static string Quote(string arg) {
            if(arg==null)arg="";var b=new StringBuilder("\"");int slashes=0;
            foreach(char c in arg){if(c=='\\'){slashes++;continue;}if(c=='"'){b.Append('\\',slashes*2+1);b.Append('"');slashes=0;}else{b.Append('\\',slashes);slashes=0;b.Append(c);}}
            b.Append('\\',slashes*2);b.Append('"');return b.ToString();
        }
        private static string Limit(string value, int max)
        {
            if (value == null) return null;
            if (value.Length <= max) return value;
            return value.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        private void OnErrorData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (_lock)
            {
                if (_stderr == null || !Object.ReferenceEquals(sender,_ffmpeg)) return;
                if (_stderr.Length > 2000) _stderr.Length = 2000;
                _stderr.AppendLine(e.Data);
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            lock (_lock)
            {
                if(!Object.ReferenceEquals(sender,_ffmpeg))return;_processExited = true;
                try { _processExitCode = _ffmpeg == null ? -1 : _ffmpeg.ExitCode; }
                catch { _processExitCode = -1; }
            }
        }

        private void Fail(string reason)
        {
            string msg;
            lock (_lock)
            {
                if (_failureNotified) return;
                _failureNotified = true;
                msg = Limit(reason, 200);
            }
            try{
                string logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Priint","Logs");Directory.CreateDirectory(logs);
                File.WriteAllText(Path.Combine(logs,"failed-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".txt"),reason+Environment.NewLine+(PerformanceSummary??"Falha durante preparação."));
            }catch{}
            NotifyFailed(msg);
        }

        private void NotifyStatus(string message)
        {
            Action<string> handler = Status;
            if (handler == null) return;
            Post(() => handler(message));
        }

        private void NotifyFailed(string message)
        {
            Action<string> handler = Failed;
            if (handler == null) return;
            Post(() => handler(message));
        }

        private void Post(Action invoke)
        {
            var ctx = _ui;
            if (ctx != null) ctx.Post(_ => invoke(), null);
            else invoke();
        }
    }
}









