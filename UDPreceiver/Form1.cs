using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Timers;
using System.Diagnostics;
using System.Management;



namespace UDPreceiver
{
    public partial class Form1 : Form
    {
        // 各windowの状態フラグ
        Boolean isPartialWindowShown;
        Boolean isFullWindowShown;

        // cmd用定数
        public const Byte END = 0;
        public const Byte PARTW = 1;
        public const Byte FULLW = 2;
        public const Byte NOIMG = 3;
        public const Byte MUTE = 4;
        public const Byte VDWN = 5;
        // cmd長
        public const Byte CMDLEN = 5;

        // partial window size 定数
        public const Int32 pww = 400;
        public const Int32 pwh = 400;

        // 使用ポート番号
        public const Int32 listenPort = 11006;

        // timeout定数
        public const Int32 TIMEOUT = 10; // seconds

        // partial windowに表示する用
        Graphics gg;
        Bitmap gbmp;

        // full windowに表示する用
        Graphics fg;
        Bitmap fbmp;

        // timeout監視タイマ
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        // 最終パケット受信時刻
        DateTime lastDate = new DateTime();

        // socketリスナー
        IPEndPoint groupEP;
        UdpClient listener;

        // full screen mode用Form
        Form fullform = new Form();

        // full screen mode用picturebox
        PictureBox fullpBox = new PictureBox();

        // full screen mode 用 label
        //Label flabel1 = new Label();

        // debug
        Stopwatch sw = new Stopwatch();

        // receive queue
        Queue<byte[]> rcvq = new Queue<byte[]>();

        // keyboard hook
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT lParam);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, int dwThreadId);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hHook, int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hHook);

        // volume control 用
        private const int APPCOMMAND_VOLUME_MUTE = 0x80000;
        private const int APPCOMMAND_VOLUME_UP = 0xA0000;
        private const int APPCOMMAND_VOLUME_DOWN = 0x90000;
        private const int WM_APPCOMMAND = 0x319;
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        //
        public sealed class KeybordCaptureEventArgs : EventArgs
        {
            private int m_keyCode;
            private int m_scanCode;
            private int m_flags;
            private int m_time;
            private bool m_cancel;

            internal KeybordCaptureEventArgs(KBDLLHOOKSTRUCT keyData)
            {
                this.m_keyCode = keyData.vkCode;
                this.m_scanCode = keyData.scanCode;
                this.m_flags = keyData.flags;
                this.m_time = keyData.time;
                this.m_cancel = false;
            }

            public int KeyCode { get { return this.m_keyCode; } }
            public int ScanCode { get { return this.m_scanCode; } }
            public int Flags { get { return this.m_flags; } }
            public int Time { get { return this.m_time; } }
            public bool Cancel
            {
                set { this.m_cancel = value; }
                get { return this.m_cancel; }
            }
        }
        // keyboard hook consts
        public const int WH_KEYBOARD_LL = 13;
        public const int HC_ACTION = 0;
        // keyboard hook variables
        private static IntPtr s_hook;
        private static LowLevelKeyboardProc s_proc;

        public Form1()
        {
            InitializeComponent();

            // partial Formはリサイズ禁止
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;

            // Partial Form コントロールボックス非表示
            this.ControlBox = false;

            // ダブルクリックによる最大化禁止
            this.MaximizeBox = false;

            // Partial Formを自動リサイズに
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // Partial FormのClientArea resize
            // this.ClientSize = new Size(pww, pwh);

            // Partial Form の pictureboxをリサイズ
            this.pictureBox1.Size = new Size(pww, pwh);
            //this.pictureBox1.Dock = DockStyle.Fill;

            // Partial Formはタスクバーに表示しない
            this.ShowInTaskbar = false;

            // Partial Formは常に手前に表示
            this.TopMost = true;

            // Full Form をフルスクリーンに
            fullform.FormBorderStyle = FormBorderStyle.None;
            fullform.WindowState = FormWindowState.Maximized;

            // Full Formはタスクバーに表示しない
            fullform.ShowInTaskbar = false;

            // Full Formは常に手前に表示
            fullform.TopMost = true;

            // Full Formがキーイベントを取得
            fullform.KeyPreview = true;

            //
            //fullform.Controls.Add(flabel1);

            // Full Formにpictureboxを追加
            fullform.Controls.Add(fullpBox);


            // pictureboxはFull Formいっぱいに示表  
            fullpBox.Dock = DockStyle.Fill;

            // FormClosingのイベントハンドラを追加
            this.FormClosing += new FormClosingEventHandler(FormClosingHandle);
            fullform.FormClosing += new FormClosingEventHandler(FormClosingHandle);

            // 全方向にアンカー、ウインドウ全体に表示
            //fullpBox.Anchor = (AnchorStyles.Bottom | AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);

            // windowsは閉じている
            isPartialWindowShown = false;
            isFullWindowShown = false;

            // Formを表示(仮)
            //this.Show();

            // partial window表示用のgbmpとgg初期化
            gbmp = new Bitmap(pww, pwh);
            gg = this.CreateGraphics();
            gg = Graphics.FromImage(gbmp);

            // full window用のbmpとグラフィックス初期化
            Int32 ymax = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
            Int32 xmax = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            fbmp = new Bitmap(xmax, ymax);
            fg = this.CreateGraphics();
            fg = Graphics.FromImage(fbmp);

            // 親Formの設定
            Form f = new Form();
            f.ShowInTaskbar = false;
            f.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            f.Opacity = 0;
            f.Show();
            this.Owner = f;
            fullform.Owner = f;

            // はじめにマスターボリュームをミュート
            //GetMasterVolumeMute();

            // Baloon Tip
            //this.notifyIcon1.ShowBalloonTip(500);

            // socketの準備
            try
            {
                listener = new UdpClient(listenPort);
                groupEP = new IPEndPoint(IPAddress.Any, listenPort);
            }
            catch (Exception er)
            {
                MessageBox.Show("ソケットが開けません。二重に起動しているか、他のプログラムがポートを使用しています。");
                Environment.Exit(0);
            }

            // リスナスレッドの開始
            Thread rec = new Thread(Receiver);
            rec.Start();

            // ドロワスレッドの開始
            Thread draw = new Thread(Drawer);
            draw.Start();

            // timeout監視タイマの開始
            timer.Tick += new EventHandler(TimeoutWatcher);
            timer.Interval = TIMEOUT * 1000 / 2; // milliseconds; タイムアウトの半分(標本化定理)
            timer.Enabled = true;
            timer.Start();

            // keyboard フックの追加

            s_hook = SetWindowsHookEx(WH_KEYBOARD_LL,
            s_proc = new LowLevelKeyboardProc(HookProc),
            System.Runtime.InteropServices.Marshal.GetHINSTANCE(typeof(Form1).Module),
            //Native.GetModuleHandle(null),
            0);
            AppDomain.CurrentDomain.DomainUnload += delegate
            {
                if (s_hook != IntPtr.Zero)
                    UnhookWindowsHookEx(s_hook);
            };
        }

        // keyboard hookの関数
        private IntPtr HookProc(int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT lParam)
        {
            bool cancel = false;
            if (nCode == HC_ACTION)
            {
                KeybordCaptureEventArgs ev = new KeybordCaptureEventArgs(lParam);
                cancel = ev.Cancel;
            }
            if (isFullWindowShown)
            {
                // キー入力はすべて破棄
                return (IntPtr)1;
            }
            else
            {   // 次のフックを呼ぶ
                return cancel ? (IntPtr)1 : CallNextHookEx(s_hook, nCode, wParam, ref lParam);
            }
        }


        private void TimeoutWatcher(object sender, EventArgs e)
        {
            if (!Environment.UserInteractive) // ユーザインタラクティブじゃなかったら (画面がなければ)
                Environment.Exit(0); // 終了

            // 最後の受信からTIMEOUT秒経過したら、送信が終わっているものとみなして
            // Windowを閉じる(終了パケットのとりのがし対策)
            if ((DateTime.Now - lastDate).TotalSeconds > TIMEOUT)
            {
                if (isPartialWindowShown)
                    HidePartialWindow();
                if (isFullWindowShown)
                    HideFullWindow();
            }

            // 実行ユーザーが画面をとれているかどうかのチェック
            try
            {
                ManagementObjectSearcher oMS = new ManagementObjectSearcher();
                ManagementObjectCollection oMC;
                string sMsgStr = "";
                string[] user;
                string ConsoleUserName = "";

                oMS.Query.QueryString = "SELECT * FROM Win32_ComputerSystem";
                oMC = oMS.Get();

                foreach (ManagementObject oMO in oMC)
                {
                    user = oMO["UserName"].ToString().Split('\\');
                    sMsgStr += "Logon User:" + user[1] + "\n";
                    ConsoleUserName = user[1];
                }

                if (ConsoleUserName != Environment.UserName)
                { // コンソールユーザーとプログラム実行ユーザーが異なる = 「裏」
                    // かつポートが開いている場合、閉じる
                    Environment.Exit(0); // 終了
                }
            }
            catch (Exception er)
            {
                // たぶん「表」にだれもいない = null
                Environment.Exit(0); // 終了
            }
        }

        private void Drawer(Object state)
        {
            while (true)
            {
                if (rcvq.Count == 0) // if the receiving queue is emptyら
                {
                    Thread.Sleep(10); // sleep 10ms
                    continue;
                }

                byte[] receive_byte_array;
                receive_byte_array = rcvq.Dequeue();

                Byte[] cmd = new Byte[CMDLEN];
                //Byte[] message = new Byte[receive_byte_array.Length-cmd.Length];

                Array.Copy(receive_byte_array, cmd, cmd.Length);
                //Array.Copy(receive_byte_array, cmd.Length, message, 0, receive_byte_array.Length-cmd.Length);

                Byte op = cmd[0];
                Byte bSize = cmd[1];
                Byte bNum = cmd[2];
                Byte x = cmd[3];
                Byte y = cmd[4];

                Int32 blockSize = bSize * bNum;

                if (op == PARTW) // 部分ウインドウパケットを受信
                {
                    //sw.Restart();

                    lastDate = DateTime.Now;
                    if (!isPartialWindowShown)
                    {
                        ShowPartialWindow();
                    }
                    if (isFullWindowShown)
                        HideFullWindow();

                    MemoryStream ms = new MemoryStream(receive_byte_array, CMDLEN, receive_byte_array.Length - CMDLEN);
                    Image bmp = Image.FromStream(ms);
                    gg.DrawImage(bmp, blockSize * x, blockSize * y);
                    //gbmp = new Bitmap(ms);
                    ShowBmp();

                    //sw.Stop();
                    //ShowLbl(sw.ElapsedMilliseconds + "ms");
                }
                else if (op == FULLW) //全画面ウインドウパケットを受信
                {
                    //sw.Restart();

                    lastDate = DateTime.Now;
                    if (isPartialWindowShown)
                        HidePartialWindow();
                    if (!isFullWindowShown)
                    {
                        ShowFullWindow();
                    }

                    MemoryStream ms = new MemoryStream(receive_byte_array, CMDLEN, receive_byte_array.Length - CMDLEN);
                    Image bmp = Image.FromStream(ms);
                    fg.DrawImage(bmp, blockSize * x, blockSize * y);
                    //fbmp = new Bitmap(ms);
                    ShowFullBmp();

                    //sw.Stop();
                    //ShowFLbl(sw.ElapsedMilliseconds + "ms");
                }
                else if (op == NOIMG)
                {
                    lastDate = DateTime.Now;
                }
                else if (op == MUTE)
                {
                    // mute the volume
                    GetMasterVolumeMute();
                }
                else if (op == VDWN)
                {
                    // down the volume
                    GetMasterVolumeDown();
                }
                else if (op == END)
                {
                    if (isPartialWindowShown)
                        HidePartialWindow();
                    if (isFullWindowShown)
                        HideFullWindow();
                }
            }
        }


        private void Receiver(Object state)
        {
            try
            {
                while (true)
                {
                    //string received_data;
                    byte[] receive_byte_array;

                    // waiting for receiving data
                    receive_byte_array = listener.Receive(ref groupEP);

                    // enqueue
                    rcvq.Enqueue(receive_byte_array);

                    /*
                    
                    
                    Byte[] cmd = new Byte[CMDLEN];
                    //Byte[] message = new Byte[receive_byte_array.Length-cmd.Length];

                    Array.Copy(receive_byte_array, cmd, cmd.Length);
                    //Array.Copy(receive_byte_array, cmd.Length, message, 0, receive_byte_array.Length-cmd.Length);

                    Byte op = cmd[0];
                    Byte bSize = cmd[1];
                    Byte bNum = cmd[2];
                    Byte x = cmd[3];
                    Byte y = cmd[4];

                    Int32 blockSize = bSize * bNum;

                    if (op == PARTW) // 部分ウインドウパケットを受信
                    {
                        sw.Restart();

                        lastDate = DateTime.Now;
                        if (! isPartialWindowShown)
                            ShowPartialWindow();
                        if (isFullWindowShown)
                            HideFullWindow();

                        MemoryStream ms = new MemoryStream(receive_byte_array, CMDLEN, receive_byte_array.Length-CMDLEN);
                        Image bmp = Image.FromStream(ms);
                        gg.DrawImage(bmp, blockSize * x, blockSize * y);
                        //gbmp = new Bitmap(ms);
                        ShowBmp();

                        sw.Stop();
                        ShowLbl(sw.ElapsedMilliseconds + "ms");
                    }
                    else if (op == FULLW) //全画面ウインドウパケットを受信
                    {
                        sw.Restart();

                        lastDate = DateTime.Now;
                        if (isPartialWindowShown)
                            HidePartialWindow();
                        if (! isFullWindowShown)
                            ShowFullWindow();

                        MemoryStream ms = new MemoryStream(receive_byte_array, CMDLEN, receive_byte_array.Length-CMDLEN);
                        Image bmp = Image.FromStream(ms);
                        fg.DrawImage(bmp, blockSize * x, blockSize * y);
                        //fbmp = new Bitmap(ms);
                        ShowFullBmp();

                        sw.Stop();
                        ShowFLbl(sw.ElapsedMilliseconds + "ms");
                    }
                    else if (op == NOIMG)
                    {
                        lastDate = DateTime.Now;
                    }
                    else if (op == END)                        
                    {
                        if (isPartialWindowShown)
                            HidePartialWindow();
                        if (isFullWindowShown)
                            HideFullWindow();
                    }
                    
                     */
                }
            }
            catch (Exception e)
            {
                //Console.WriteLine(e.ToString());
                MessageBox.Show(e.ToString());
            }

        }

        private void ShowLbl(String msg)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate () { ShowLbl(msg); }));
            }
            else
            {
                //this.label1.Text = msg;
            }
        }

        private void ShowFLbl(String msg)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate () { ShowFLbl(msg); }));
            }
            else
            {
                //this.flabel1.Text = msg;
            }
        }

        private void ShowBmp()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate () { ShowBmp(); }));
            } else {
                //gg.DrawImage(bmp, blockSize*x, blockSize*y);
                this.pictureBox1.Image = gbmp;
            }
        }

        private void ShowFullBmp()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate () { ShowFullBmp(); }));
            }
            else
            {
                //fg.DrawImage(bmp, blockSize * x, blockSize * y);
                this.fullpBox.Image = fbmp;
            }
        }

        private void ShowPartialWindow()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate () { ShowPartialWindow(); }));
            }
            else
            {
                this.Show();
                isPartialWindowShown = true;
                System.Windows.Forms.Cursor.Show();
            }
        }

        private void HidePartialWindow()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate () { HidePartialWindow(); }));
            }
            else
            {
                this.Hide();
                isPartialWindowShown = false;
            }
        }

        private void ShowFullWindow()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate () { ShowFullWindow(); }));
            }
            else
            {
                fullform.Show();
                isFullWindowShown = true;
                System.Windows.Forms.Cursor.Hide();
            }
        }

        private void HideFullWindow()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate () { HideFullWindow(); }));
            }
            else
            {
                fullform.Hide();
                isFullWindowShown = false;
                System.Windows.Forms.Cursor.Show();
            }
        }

        private void FormClosingHandle(object sender, FormClosingEventArgs e)
        {
            switch (e.CloseReason)
            {
                case CloseReason.UserClosing:
                    e.Cancel = true;
                    break;
                default:
                    //Console.WriteLine("未知の理由");
                    break;
            }
        }

        private void GetMasterVolumeMute()
        {
            SendMessageW(this.Handle, WM_APPCOMMAND, this.Handle,
            (IntPtr) APPCOMMAND_VOLUME_MUTE);
        }

        private void GetMasterVolumeDown()
        {
            // it is equivalent to pressing the volume down button twice 
                SendMessageW(this.Handle, WM_APPCOMMAND, this.Handle,
                    (IntPtr)APPCOMMAND_VOLUME_DOWN);
                SendMessageW(this.Handle, WM_APPCOMMAND, this.Handle,
                    (IntPtr)APPCOMMAND_VOLUME_DOWN);
        }
    }

}

