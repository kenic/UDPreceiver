using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace UDPreceiver
{
    static class Program
    {
        /// <summary>
        /// アプリケーションのメイン エントリ ポイントです。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Form f = new Form1();
            Application.Run();
            //Application.Run(new Form1());
        }
    }
}
