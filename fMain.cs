using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenRecord
{
    public partial class fMain : Form
    {
        private Process _ffmpegProcess;
        private StringBuilder _ffmpegLog = new StringBuilder();
        private System.Windows.Forms.Timer _previewTimer;
        private Button btnRefreshDevices;

        public fMain()
        {
            InitializeComponent();
            // 确保窗体加载时能填充列表（防止 Load 事件未在设计器中连接的情况）
            try
            {
                PopulateAudioDeviceList();
            }
            catch { }
            try
            {
                ShowCaptureTargetPicker();
            }
            catch { }
            // 允许在 listBox2 中多选，以便录制多个音源（例如 MIC + 扬声器）
            try
            {
                if (this.listBox2 != null)
                    this.listBox2.SelectionMode = SelectionMode.MultiExtended;
            }
            catch { }
            // 启用键盘预览并绑定全局快捷键处理（Alt+S 开始，Alt+T 停止）
            try
            {
                this.KeyPreview = true;
                this.KeyDown += fMain_KeyDown;
            }
            catch { }
            try
            {
                if (this.btnRefreshDevices == null)
                {
                    btnRefreshDevices = new Button { Name = "btnRefreshDevices", Text = "刷新音频设备", Size = new Size(110, 28) };
                    btnRefreshDevices.Click += (s, e) => PopulateAudioDeviceList();
                    // 尝试放在 listBox2 旁边，否则放在顶部右侧的默认位置
                    if (this.listBox2 != null)
                    {
                        btnRefreshDevices.Location = new Point(Math.Min(this.listBox2.Right + 8, this.Width - btnRefreshDevices.Width - 8), this.listBox2.Top);
                        btnRefreshDevices.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    }
                    else
                    {
                        btnRefreshDevices.Location = new Point(8, 8);
                        btnRefreshDevices.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    }
                    this.Controls.Add(btnRefreshDevices);
                }
            }
            catch
            {
                // 忽略动态创建失败，用户可在设计器手动添加按钮
            }
            try
            {
                if (this.pictureBox1 != null)
                {
                    this.pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
                }
                _previewTimer = new System.Windows.Forms.Timer { Interval = 500 };
                _previewTimer.Tick += PreviewTimer_Tick;
                if (this.listBox1 != null)
                {
                    this.listBox1.SelectedIndexChanged += (s, e) => {
                        // 选择改变后立即刷新一次并启动定时更新
                        RefreshPreviewOnce();
                        _previewTimer.Start();
                    };
                }
            }
            catch { }
        }

        private void fMain_Load(object sender, EventArgs e)
        {
            // 窗体加载时预先填充音频设备和视频选择列表
            try
            {
                PopulateAudioDeviceList();
            }
            catch { }
            try
            {
                ShowCaptureTargetPicker();
            }
            catch { }
            bStart.Enabled = true;
            bStop.Enabled = false;
        }

        private void TryEnableStereoMixDevices()
        {
            // This operation may require admin rights and is best-effort.
            // We'll try to enable devices named "立体声混音" or "Stereo Mix" using PowerShell's Get-PnpDevice / Enable-PnpDevice when available.
            try
            {
                var ps = """
                $names = @('立体声混音','Stereo Mix')
                foreach ($n in $names) {
                    $devs = Get-PnpDevice -FriendlyName $n -ErrorAction SilentlyContinue
                    if ($devs) {
                        foreach ($d in $devs) {
                            if ($d.Status -eq 'Error' -or $d.Status -eq 'Unknown' -or $d.Status -eq 'Disabled') {
                                Enable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction SilentlyContinue
                            }
                        }
                    }
                }
                """;

                // Write ps to temp file and execute via powershell.exe
                var temp = Path.Combine(Path.GetTempPath(), "enable_stereomix.ps1");
                File.WriteAllText(temp, ps, Encoding.UTF8);
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{temp}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(3000);
                try
                {
                    File.Delete(temp);
                }
                catch { }
            }
            catch
            {
                // ignore; if it fails, user will need to enable manually
            }
        }

        private (string kind, Rectangle bounds, string windowTitle) ShowCaptureTargetPicker()
        {
            // 如果控件不存在，回退到默认（整个桌面）
            if (this.listBox1 == null)
            {
                return ("desktop", Rectangle.Empty, null);
            }

            // 填充 listBox1
            try
            {
                listBox1.BeginUpdate();
                listBox1.Items.Clear();
                listBox1.Items.Add("桌面 (整个显示器)");
                // 每个屏幕
                foreach (var scr in Screen.AllScreens)
                {
                    listBox1.Items.Add($"屏幕: {scr.DeviceName} ({scr.Bounds.Width}x{scr.Bounds.Height})");
                }

                // 窗口
                var windows = EnumTopLevelWindows();
                if (windows.Count > 0)
                {
                    //listBox1.Items.Add("-- 窗口 列表 --");
                    foreach (var w in windows)
                    {
                        listBox1.Items.Add("窗口: " + w);
                    }
                }

                // 保持现有选择或默认选第一个
                if (listBox1.SelectedIndex < 0 && listBox1.Items.Count > 0)
                    listBox1.SelectedIndex = 0;
            }
            finally
            {
                listBox1.EndUpdate();
            }

            // 返回当前选中项（由用户在主界面直接选择），解析结果与原来一致
            var sel = listBox1.SelectedItem as string;
            if (sel == null)
                return (null, Rectangle.Empty, null);
            if (sel.StartsWith("桌面"))
                return ("desktop", Rectangle.Empty, null);
            if (sel.StartsWith("屏幕:"))
            {
                var parts = sel.Split(':');
                var name = parts[1].Trim().Split(' ')[0];
                var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) ?? Screen.PrimaryScreen;
                return ("screen", screen.Bounds, null);
            }
            if (sel.StartsWith("窗口:"))
            {
                var title = sel.Substring("窗口: ".Length);
                return ("window", Rectangle.Empty, title);
            }
            return (null, Rectangle.Empty, null);
        }

        private List<string> EnumTopLevelWindows()  // 枚举顶级窗口，返回标题列表。只列出可见且有标题的窗口，跳过“程序管理器”。
        {
            var list = new List<string>();
            EnumWindows((hwnd, lParam) => {
                if (!IsWindowVisible(hwnd))
                    return true;
                var len = GetWindowTextLength(hwnd);
                if (len == 0)
                    return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title))
                    return true;
                // skip shell or program manager
                if (title == "Program Manager")
                    return true;
                list.Add(title);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private enum EDataFlow
        {
            eRender = 0, eCapture = 1, eAll = 2
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
            // other methods not used
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-C0EC0F6C9B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig]
            int GetCount(out uint pcDevices);
            [PreserveSig]
            int Item(uint nDevice, out IMMDevice ppDevice);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            [PreserveSig]
            int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            [PreserveSig]
            int GetState(out uint pdwState);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint cProps);
            [PreserveSig]
            int GetAt(uint iProp, out PROPERTYKEY pkey);
            [PreserveSig]
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            [PreserveSig]
            int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            [PreserveSig]
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid; public uint pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public ushort vt; public ushort wReserved1; public ushort wReserved2; public ushort wReserved3; public IntPtr p1; public IntPtr p2;
        }

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT pvar);

        // PROPERTYKEY for PKEY_Device_FriendlyName
        private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

        private class CaptureDeviceInfo
        {
            public string id; public string name; public uint state;
        }

        private List<CaptureDeviceInfo> GetCaptureDevices()
        {
            var list = new List<CaptureDeviceInfo>();
            try
            {
                var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
                var type = Type.GetTypeFromCLSID(clsid);
                var obj = Activator.CreateInstance(type);
                var enumerator = (IMMDeviceEnumerator)obj;
                const uint STATE_ALL = 0x0000000F; // ACTIVE | DISABLED | NOTPRESENT | UNPLUGGED
                var hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, STATE_ALL, out var collection);
                if (hr != 0 || collection == null)
                    return list;
                hr = collection.GetCount(out uint count);
                if (hr != 0)
                    return list;
                for (uint i = 0; i < count; i++)
                {
                    hr = collection.Item(i, out var device);
                    if (hr != 0 || device == null)
                        continue;
                    hr = device.GetId(out string id);
                    device.GetState(out uint state);
                    string name = null;
                    if (device.OpenPropertyStore(0, out var props) == 0 && props != null)
                    {
                        var key = PKEY_Device_FriendlyName;
                        if (props.GetValue(ref key, out var pv) == 0)
                        {
                            try
                            {
                                if (pv.vt == 31 && pv.p1 != IntPtr.Zero) // VT_LPWSTR == 31
                                {
                                    name = Marshal.PtrToStringUni(pv.p1);
                                }
                            }
                            finally
                            {
                                try
                                {
                                    PropVariantClear(ref pv);
                                }
                                catch { }
                            }
                        }
                    }
                    list.Add(new CaptureDeviceInfo { id = id, name = name ?? id, state = state });
                }
            }
            catch
            {
                // ignore and return what we have
            }
            return list;
        }

        // 将探测到的音频设备填充到窗体上的 listBox2
        private void PopulateAudioDeviceList()
        {
            if (this.listBox2 == null)
                return;
            try
            {
                listBox2.BeginUpdate();
                listBox2.Items.Clear();
                //listBox2.Items.Add("<无音频>");
                try
                {
                    var devs = GetCaptureDevices();
                    if (devs != null && devs.Count > 0)
                    {
                        listBox2.Items.Add("-- Windows 录音端点（包含已禁用） --");
                        foreach (var d in devs)
                        {
                            var stateStr = d.state == 0 ? "Unknown" : (d.state & 0x1) != 0 ? "Active" : (d.state & 0x2) != 0 ? "Disabled" : (d.state & 0x4) != 0 ? "NotPresent" : (d.state & 0x8) != 0 ? "Unplugged" : d.state.ToString();
                            // 使用 wasapi 前缀并包含设备 id 和友好名，格式: wasapi:{id}|{name} ({state})
                            // 在启动录制时我们会优先使用 id（若可用）来避免因特殊字符导致匹配失败
                            listBox2.Items.Add($"wasapi:{d.id}|{d.name} ({stateStr})");
                        }
                    }
                    else
                    {
                        // 如果 COM 枚举返回空列表，使用 ffmpeg 探测作为后备
                        var devices = ProbeAudioDevices();
                        if (devices.wasapi != null && devices.wasapi.Length > 0)
                        {
                            listBox2.Items.Add("-- WASAPI 设备 --");
                            foreach (var w in devices.wasapi)
                                listBox2.Items.Add("wasapi:" + w);
                        }
                        if (devices.dshow != null && devices.dshow.Length > 0)
                        {
                            listBox2.Items.Add("-- dshow 设备 --");
                            foreach (var d in devices.dshow)
                                listBox2.Items.Add("dshow:" + d);
                        }
                    }
                }
                catch
                {
                    // fall back to ffmpeg probe if COM enumeration throws
                    var devices = ProbeAudioDevices();
                    if (devices.wasapi != null && devices.wasapi.Length > 0)
                    {
                        listBox2.Items.Add("-- WASAPI 设备 --");
                        foreach (var w in devices.wasapi)
                            listBox2.Items.Add("wasapi:" + w);
                    }
                    if (devices.dshow != null && devices.dshow.Length > 0)
                    {
                        listBox2.Items.Add("-- dshow 设备 --");
                        foreach (var d in devices.dshow)
                            listBox2.Items.Add("dshow:" + d);
                    }
                }
                if (listBox2.SelectedIndex < 0 && listBox2.Items.Count > 0)
                    listBox2.SelectedIndex = 0;
            }
            finally
            {
                listBox2.EndUpdate();
            }
        }

        // 测试 ffmpeg 是否支持 wasapi 的 -loopback 选项
        private bool TestLoopbackSupport(string ffmpegPath)
        {
            try
            {
                var outp = RunProcessCaptureStderr(ffmpegPath, "-hide_banner -f wasapi -loopback 1 -i default -t 0.1 -f null -");
                if (string.IsNullOrEmpty(outp))
                    return true; // 无错误输出，保守认为支持
                if (outp.Contains("Unrecognized option") || outp.Contains("Option not found") || outp.Contains("Unknown option"))
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left; public int Top; public int Right; public int Bottom;
        }

        // 根据窗口标题查找 HWND
        private IntPtr FindWindowByTitle(string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hwnd, lParam) => {
                if (!IsWindowVisible(hwnd))
                    return true;
                var len = GetWindowTextLength(hwnd);
                if (len == 0)
                    return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                var t = sb.ToString();
                if (string.Equals(t, title, StringComparison.Ordinal))
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        // 从 listBox1 当前选择解析 capture 目标（不重新填充列表）
        private (string kind, Rectangle bounds, string windowTitle) GetSelectedCaptureFromList()
        {
            if (this.listBox1 == null)
                return ("desktop", Rectangle.Empty, null);
            var sel = listBox1.SelectedItem as string;
            if (sel == null)
                return (null, Rectangle.Empty, null);
            if (sel.StartsWith("桌面"))
                return ("desktop", Rectangle.Empty, null);
            if (sel.StartsWith("屏幕:"))
            {
                var parts = sel.Split(':');
                var name = parts[1].Trim().Split(' ')[0];
                var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) ?? Screen.PrimaryScreen;
                return ("screen", screen.Bounds, null);
            }
            if (sel.StartsWith("窗口:"))
            {
                var title = sel.Substring("窗口: ".Length);
                return ("window", Rectangle.Empty, title);
            }
            return (null, Rectangle.Empty, null);
        }

        // Capture window using PrintWindow if available, otherwise fall back to screen copy
        private Bitmap CaptureWindowBitmap(string title)
        {
            try
            {
                var hwnd = FindWindowByTitle(title);
                if (hwnd == IntPtr.Zero)
                    return null;
                if (!GetWindowRect(hwnd, out var r))
                    return null;
                var w = Math.Max(1, r.Right - r.Left);
                var h = Math.Max(1, r.Bottom - r.Top);
                var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    bool ok = false;
                    try
                    {
                        ok = PrintWindow(hwnd, hdc, 0);
                    }
                    finally
                    {
                        try
                        {
                            g.ReleaseHdc(hdc);
                        }
                        catch { }
                    }
                    if (!ok)
                    {
                        bmp.Dispose();
                        var bmp2 = new Bitmap(w, h);
                        using (var g2 = Graphics.FromImage(bmp2))
                        {
                            g2.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                        }
                        return bmp2;
                    }
                }
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private Bitmap CaptureRectangle(Rectangle rect)
        {
            try
            {
                if (rect.Width <= 0 || rect.Height <= 0)
                    return null;
                var bmp = new Bitmap(rect.Width, rect.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
                }
                return bmp;
            }
            catch { return null; }
        }

        private void RefreshPreviewOnce()
        {
            try
            {
                var sel = GetSelectedCaptureFromList();
                Bitmap bmp = null;
                if (sel.kind == "desktop")
                {
                    var vs = SystemInformation.VirtualScreen;
                    bmp = CaptureRectangle(vs);
                }
                else if (sel.kind == "screen")
                {
                    bmp = CaptureRectangle(sel.bounds);
                }
                else if (sel.kind == "window")
                {
                    bmp = CaptureWindowBitmap(sel.windowTitle);
                }
                if (bmp != null && this.pictureBox1 != null)
                {
                    var old = pictureBox1.Image;
                    pictureBox1.Image = (Bitmap)bmp;
                    try
                    {
                        old?.Dispose();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            RefreshPreviewOnce();
        }

        // 在ffmpeg中查找可用的音频设备（dshow和wasapi）。返回列表的元组。
        // ffmpeg 列出设备的格式比较混乱，直接解析文本输出，提取设备名称（用引号括起的部分）。
        // dshow 和 wasapi 的输出格式略有不同，需要分别处理。
        private (string[] dshow, string[] wasapi) ProbeAudioDevices()
        {
            try
            {
                var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                    ffmpegPath = "ffmpeg.exe";

                // dshow
                var dshowInfo = RunProcessCaptureStderr(ffmpegPath, "-list_devices true -f dshow -i dummy");
                var dshow = ParseDshowDevices(dshowInfo);

                // wasapi
                var wasapiInfo = RunProcessCaptureStderr(ffmpegPath, "-list_devices true -f wasapi -i dummy");
                var wasapi = ParseWasapiDevices(wasapiInfo);

                return (dshow, wasapi);
            }
            catch
            {
                return (new string[0], new string[0]);
            }
        }

        private string RunProcessCaptureStderr(string file, string args)  // 启动进程，捕获标准错误输出，返回文本
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    StandardErrorEncoding = Encoding.Default,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(2000);
                return stderr ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string[] ParseDshowDevices(string stderr)  //包含"DirectShow audio devices"的行之后列出了dshow设备，设备名称用引号括起
        {
            if (string.IsNullOrWhiteSpace(stderr))
                return new string[0];
            var lines = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = lines.Where(l => l.Contains("DirectShow audio devices") == false && l.Contains("Alternative name") == false)
                            .Where(l => l.Contains("\""))
                            .Select(l => {
                                var i1 = l.IndexOf('"');
                                var i2 = l.IndexOf('"', i1 + 1);
                                if (i1 >= 0 && i2 > i1)
                                    return l.Substring(i1 + 1, i2 - i1 - 1);
                                return null;
                            })
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Distinct()
                            .ToArray();
            return list;
        }

        private string[] ParseWasapiDevices(string stderr)  //包含"WASAPI"的行中列出了WASAPI设备，设备名称用引号括起
        {
            if (string.IsNullOrWhiteSpace(stderr))
                return new string[0];
            var lines = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = lines.Where(l => l.Contains("wasapi") || l.Contains("WASAPI"))
                            .Where(l => l.Contains('\"'))
                            .Select(l => {
                                var i1 = l.IndexOf('"');
                                var i2 = l.LastIndexOf('"');
                                if (i1 >= 0 && i2 > i1)
                                    return l.Substring(i1 + 1, i2 - i1 - 1);
                                return null;
                            })
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Distinct()
                            .ToArray();
            return list;
        }

        // （已用 PopulateAudioDeviceList 替代）
        private async void bStart_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "MP4 files (*.mp4)|*.mp4";
                sfd.FileName = "recording.mp4";
                if (sfd.ShowDialog() != DialogResult.OK)
                    return;
                var outputPath = sfd.FileName;

                // 尝试在应用目录寻找 ffmpeg.exe，否则使用系统 PATH
                var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                    ffmpegPath = "ffmpeg.exe";

                // 使用已在界面 listBox1 中的选择作为捕获目标（避免在开始时重新填充 listBox1 导致选择重置）
                var captureSelection = GetSelectedCaptureFromList();
                if (captureSelection.kind == null)
                    return; // 没有选择任何捕获目标

                // 使用 listBox2 的项作为音频选择（支持多选）
                // 如果 checkbox1 被勾选，表示用户选择“仅录制视频”，跳过音频枚举/恢复逻辑
                string[] savedSelectedCore = null;
                var savedItem = (object)null;
                var savedIndex = -1;
                bool skipAudio = checkBox1 != null && checkBox1.Checked;
                if (!skipAudio)
                {
                    try
                    {
                        if (this.listBox2 != null)
                        {
                            savedSelectedCore = listBox2.SelectedItems.Cast<object>()
                                .Select(o => o as string)
                                .Where(s => !string.IsNullOrEmpty(s) && s != "<无音频>")
                                .Select(s => {
                                    var idx = s.IndexOf(" (");
                                    return idx >= 0 ? s.Substring(0, idx) : s;
                                })
                                .ToArray();
                        }
                    }
                    catch { savedSelectedCore = null; }

                    TryEnableStereoMixDevices();
                    // 保存当前选中项/索引（以便在不能按文本匹配时回退）
                    savedItem = this.listBox2?.SelectedItem;
                    savedIndex = this.listBox2?.SelectedIndex ?? -1;

                    // 重新填充（如果确实需要）
                    PopulateAudioDeviceList();

                    // 尝试基于核心文本恢复多选；若未恢复则使用原有的对象或索引回退
                    try
                    {
                        if (savedSelectedCore != null && this.listBox2 != null)
                        {
                            for (int i = 0; i < listBox2.Items.Count; i++)
                            {
                                var it = listBox2.Items[i] as string;
                                if (string.IsNullOrEmpty(it))
                                    continue;
                                var core = it.IndexOf(" (") >= 0 ? it.Substring(0, it.IndexOf(" (")) : it;
                                if (savedSelectedCore.Any(s => string.Equals(s, core, StringComparison.OrdinalIgnoreCase)))
                                    listBox2.SetSelected(i, true);
                            }
                        }
                    }
                    catch { }

                    // 尝试基于原始对象或索引恢复单选（回退方案）
                    if ((listBox2.SelectedIndex < 0 || listBox2.SelectedItems.Count == 0) && savedItem != null)
                    {
                        var newIdx = this.listBox2.Items.IndexOf(savedItem);
                        if (newIdx >= 0)
                            this.listBox2.SelectedIndex = newIdx;
                        else if (savedIndex >= 0 && savedIndex < this.listBox2.Items.Count)
                            this.listBox2.SelectedIndex = savedIndex;
                    }
                }

                // 收集多个音频输入参数（每个元素包含其 -f ... -i ... 部分）
                List<string> audioInputArgs = new List<string>();
                try
                {
                    // 如果用户选择了仅录视频（skipAudio == true），则跳过收集音频输入
                    if (!skipAudio && this.listBox2 != null)
                    {
                        var selected = listBox2.SelectedItems.Cast<object>().Select(o => o as string).Where(s => !string.IsNullOrEmpty(s) && s != "<无音频>").ToArray();
                        if (selected.Length > 0)
                        {
                            var supportsLoop = TestLoopbackSupport(ffmpegPath);
                            foreach (var sel in selected)
                            {
                                if (sel.StartsWith("wasapi:"))
                                {
                                    var payload = sel.Substring("wasapi:".Length);
                                    var idxState = payload.IndexOf(" (");
                                    var core = idxState >= 0 ? payload.Substring(0, idxState) : payload;
                                    string id = null, name = null;
                                    var parts = core.Split(new[] { '|' }, 2);
                                    if (parts.Length == 2)
                                    {
                                        id = parts[0];
                                        name = parts[1];
                                    }
                                    else
                                    {
                                        name = core;
                                    }
                                    var deviceSpecifier = !string.IsNullOrEmpty(id) ? id : name;
                                    var inArg = supportsLoop ? $"-f wasapi -loopback 1 -i \"{deviceSpecifier}\"" : $"-f wasapi -i \"{deviceSpecifier}\"";
                                    audioInputArgs.Add(inArg);
                                }
                                else if (sel.StartsWith("dshow:"))
                                {
                                    var name = sel.Substring("dshow:".Length);
                                    audioInputArgs.Add($"-f dshow -i audio=\"{name}\"");
                                }
                                else
                                {
                                    // 忽略未知前缀
                                }
                            }
                        }
                    }
                }
                catch
                {
                    audioInputArgs.Clear();
                }

                // 构建 gdigrab 输入参数，根据用户选择的捕获目标
                string videoInputArgs;
                if (captureSelection.kind == "desktop")
                {
                    videoInputArgs = $"-f gdigrab -framerate 30 -i desktop";  // 捕获整个桌面
                }
                else if (captureSelection.kind == "screen")  // 捕获指定屏幕
                {
                    // offset和video_size必须位于-i桌面之前
                    var b = captureSelection.bounds;  //b在屏幕坐标中，gdigrab使用offset-x/y指定捕获区域的左上角，video_size指定宽度/高度
                    videoInputArgs = $"-f gdigrab -framerate 30 -offset_x {b.Left} -offset_y {b.Top} -video_size {b.Width}x{b.Height} -i desktop";
                }
                else // window
                {
                    var titleEscaped = captureSelection.windowTitle.Replace("\"", "\\\"");
                    videoInputArgs = $"-f gdigrab -framerate 30 -i title=\"{titleEscaped}\"";
                }

                string args;
                if (audioInputArgs == null || audioInputArgs.Count == 0)
                {
                    args = $"-y {videoInputArgs} -c:v libx264 -preset veryfast -crf 18 -r 30 \"{outputPath}\""; // 无音频输入
                }
                else if (audioInputArgs.Count == 1)
                {
                    // 单一音频输入，ffmpeg 的输入顺序：video 为第一个输入(0)，audio 为第二个输入(1)
                    args = $"-y {videoInputArgs} {audioInputArgs[0]} -map 0:v -map 1:a -c:v libx264 -preset veryfast -crf 18 -r 30 -c:a aac -b:a 192k \"{outputPath}\"";
                }
                else
                {
                    // 多个音频输入，使用 filter_complex 的 amix 合并音轨
                    var sb = new StringBuilder();
                    sb.Append("-y ");
                    sb.Append(videoInputArgs);
                    sb.Append(' ');
                    foreach (var ai in audioInputArgs)
                    {
                        sb.Append(ai);
                        sb.Append(' ');
                    }
                    // 构造 amix 输入标签： [1:a][2:a]...
                    var inputLabels = string.Concat(Enumerable.Range(1, audioInputArgs.Count).Select(i => $"[{i}:a]"));
                    var filter = $"{inputLabels}amix=inputs={audioInputArgs.Count}:normalize=1[aout]";
                    sb.Append($"-filter_complex \"{filter}\" -map 0:v -map \"[aout]\" -c:v libx264 -preset veryfast -crf 18 -r 30 -c:a aac -b:a 192k \"{outputPath}\"");
                    args = sb.ToString();
                }

                var startInfo = new ProcessStartInfo // 启动 ffmpeg 进行录制
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.Default,
                    RedirectStandardInput = true
                };

                try
                {
                    _ffmpegProcess?.Kill();  // 如果之前有 ffmpeg 进程在运行，先杀掉它
                    bool alreadyNotified = false;
                    _ffmpegProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true }; // EnableRaisingEvents 以便能在 ffmpeg 进程退出时收到通知
                    _ffmpegProcess.ErrorDataReceived += (s, ev) => // ffmpeg 的日志主要输出在标准错误流，所以我们监听 ErrorDataReceived 事件来捕获日志
                    {
                        if (!string.IsNullOrEmpty(ev.Data))
                        {
                            lock (_ffmpegLog)   // 由于 ffmpeg 输出日志的线程可能与 UI 线程不同，我们需要锁定 _ffmpegLog 来确保线程安全
                                _ffmpegLog.AppendLine(ev.Data);  // 将 ffmpeg 的日志追加到 _ffmpegLog 中，以便后续在录制完成或出错时显示给用户参考
                        }
                    };

                    _ffmpegProcess.Exited += (s, ev) => {
                        // 捕获闭包对outputPath和alreadyNotified的本地引用
                        var outPath = outputPath;
                        this.BeginInvoke(() => {
                            try
                            {
                                if (alreadyNotified)
                                {
                                    // 已经向用户展示过开始/失败信息，仅做清理
                                }
                                else
                                {

                                    // 如果文件存在并且有大小，则报告成功
                                    if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                                    {
                                        MessageBox.Show("录制已完成：" + outPath, "录制", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    }
                                    else
                                    {
                                        var log = string.Empty;
                                        lock (_ffmpegLog) { log = _ffmpegLog.ToString(); }
                                        if (string.IsNullOrWhiteSpace(log))
                                            log = "ffmpeg 未输出错误日志，但未生成有效文件。";
                                        MessageBox.Show("录制未生成文件，ffmpeg 输出:\n" + log, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    }
                                }
                            }
                            finally
                            {
                                _ffmpegProcess?.Dispose();
                                _ffmpegProcess = null;
                                lock (_ffmpegLog)
                                    _ffmpegLog.Clear();
                            }
                        });
                    };

                    _ffmpegProcess.Start();
                    _ffmpegProcess.BeginErrorReadLine();

                    // 等待短时间，确认 ffmpeg 是否立即退出（通常设备错误会导致进程立即退出）
                    await System.Threading.Tasks.Task.Delay(700);
                    if (_ffmpegProcess == null || _ffmpegProcess.HasExited)
                    {
                        // ffmpeg 未能正常启动或已立即退出，读取日志并尝试检测是否为音频设备错误
                        var log = string.Empty;
                        lock (_ffmpegLog) { log = _ffmpegLog.ToString(); }
                        if (string.IsNullOrWhiteSpace(log)) log = "ffmpeg 启动失败或立即退出，未生成文件。";

                        // 简单关键字判断是否为音频设备相关错误
                        var lower = log.ToLowerInvariant();
                        bool audioError = false;
                        string[] audioIndicators = new[] { "cannot open audio", "cannot open audio device", "device not found", "no such file", "no such device", "error opening audio", "wasapi", "dshow", "unable to find" };
                        foreach (var k in audioIndicators)
                        {
                            if (lower.Contains(k)) { audioError = true; break; }
                        }

                        if (audioError)
                        {
                            // 提示用户并给出选项：是=重新选择音频并重试，否=仅录制视频，取消=放弃
                            var choice = MessageBox.Show("音频设备启动失败，可能是选择错误。\n\n错误信息:\n" + log + "\n\n请选择：是=重新选择音频并重试，否=仅录制视频，取消=取消录制。", "音频错误", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                            alreadyNotified = true;
                            if (choice == DialogResult.Yes)
                            {
                                // 让用户调整选择后重新点击录制
                                return;
                            }
                            else if (choice == DialogResult.No)
                            {
                                // 重新以无音频模式启动 ffmpeg
                                try
                                {
                                    var argsVideoOnly = $"-y {videoInputArgs} -c:v libx264 -preset veryfast -crf 18 -r 30 \"{outputPath}\"";
                                    var startInfo2 = new ProcessStartInfo
                                    {
                                        FileName = ffmpegPath,
                                        Arguments = argsVideoOnly,
                                        UseShellExecute = false,
                                        CreateNoWindow = true,
                                        RedirectStandardError = true,
                                        StandardErrorEncoding = Encoding.Default,
                                        RedirectStandardInput = true
                                    };
                                    _ffmpegProcess = new Process { StartInfo = startInfo2, EnableRaisingEvents = true };
                                    _ffmpegProcess.ErrorDataReceived += (s2, ev2) => { if (!string.IsNullOrEmpty(ev2.Data)) { lock (_ffmpegLog) _ffmpegLog.AppendLine(ev2.Data); } };
                                    _ffmpegProcess.Exited += (s2, ev2) => { /* 重用已有 Exited 处理逻辑 */ };
                                    _ffmpegProcess.Start();
                                    _ffmpegProcess.BeginErrorReadLine();
                                    //MessageBox.Show("已开始录制（无音频）：" + outputPath, "录制", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    bStart.Enabled = false;
                                    bStop.Enabled = true;
                                    this.WindowState = FormWindowState.Minimized;
                                }
                                catch (Exception ex2)
                                {
                                    MessageBox.Show("启动 ffmpeg（无音频模式）失败: " + ex2.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                return;
                            }
                            else
                            {
                                // 取消，不做任何事
                                return;
                            }
                        }
                        else
                        {
                            MessageBox.Show("启动 ffmpeg 失败:\n" + log, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            alreadyNotified = true;
                        }
                    }
                    else
                    {
                        //MessageBox.Show("已开始录制：" + outputPath, "录制", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        alreadyNotified = true;
                        //最小化窗口以避免录制时遮挡
                        bStart.Enabled = false;
                        bStop.Enabled = true;
                        this.WindowState = FormWindowState.Minimized;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("启动 ffmpeg 失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void bStop_Click(object sender, EventArgs e)
        {
            StopRecording();
            bStart.Enabled = true;
            bStop.Enabled = false;
        }

        // 全局快捷键处理：Alt+S 开始录制，Alt+T 停止录制
        private void fMain_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Alt && e.KeyCode == Keys.S)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    // 调用开始录制（与 UI 点击行为一致）
                    bStart_Click(this, EventArgs.Empty);
                }
                else if (e.Alt && e.KeyCode == Keys.T)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    bStop_Click(this, EventArgs.Empty);
                }
            }
            catch { }
        }

        private void StopRecording()
        {
            if (_ffmpegProcess == null)
                return;
            try
            {
                if (!_ffmpegProcess.HasExited)
                {
                    try
                    {
                        _ffmpegProcess.StandardInput.Write('q');
                    }
                    catch { }
                    if (!_ffmpegProcess.WaitForExit(5000))
                    {
                        _ffmpegProcess.Kill();
                    }
                    // 录制完成后通知用户且恢复窗口状态
                    MessageBox.Show("录制已完成。", "录制", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.WindowState = FormWindowState.Normal;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("停止录制失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;
            }
        }

    }
}
