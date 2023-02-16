using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using libzkfpcsharp;
using System.Threading;
using System.IO;
using Sample;
using ZKFPEngXControl;
using System.Drawing.Imaging;
using Oracle.DataAccess.Client; // ODP.NET Oracle managed provider
using Oracle.DataAccess.Types;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace finger_print
{
    public partial class Form1 : Form
    {
        [DllImport("user32")]
        static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
        [DllImport("user32")]
        static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

        const int MF_BYCOMMAND = 0;
        const int MF_DISABLED = 2;
        const int SC_CLOSE = 0xF060;

        IntPtr mDevHandle = IntPtr.Zero;
        IntPtr mDBHandle = IntPtr.Zero;
        IntPtr FormHandle = IntPtr.Zero;
        bool bIsTimeToDie = false;
        bool IsRegister = false;
        bool bIdentify = true;
        byte[] FPBuffer;
        int RegisterCount = 0;
        const int REGISTER_FINGER_COUNT = 3;

        byte[][] RegTmps = new byte[3][];
        byte[] RegTmp = new byte[2048];
        byte[] CapTmp = new byte[2048];

        int cbCapTmp = 2048;
        int cbRegTmp = 0;
        int iFid = 1;
        string txtTemplate1;
        int fp_m_p ;
        private int mfpWidth = 0;
        private int mfpHeight = 0;
        private int mfpDpi = 0;

        const int MESSAGE_CAPTURED_OK = 0x0400 + 6;

        [DllImport("user32.dll", EntryPoint = "SendMessageA")]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);
        ZKFPEngX fp = new ZKFPEngX();

        string[,] answer = new string[2, 10000];  int file_no = 0;
        void to_ram()
        {
            string path = @"C:\report\fdb\fp";
            DirectoryInfo d = new DirectoryInfo(path);
            FileInfo[] Files = d.GetFiles();

            foreach (FileInfo file in Files)
            {

                Byte[] finger = new Byte[2048];
                int fingerlen = 0;
                int ret = zkfp2.ExtractFromImage(mDBHandle, path + @"\" + file.Name, 500, finger, ref fingerlen) ;

                if (ret == 0)
                {
                    answer[0, file_no] = file.Name;
                    answer[1, file_no] = zkfp2.BlobToBase64(finger, fingerlen);
                    file_no++;
                   
                }
            }
        }
        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        public Form1()
        {
            InitializeComponent();
            button1.Enabled = true;
            button3.Enabled = false;
            textBox1.Visible = false;
            textBox2.Visible = false;
            button2.Visible = false;
            button4.Visible = false;
               
            fp_m_p = int.Parse(File.ReadAllText(@"C:\report\fdb\match_parcent.txt"));
            this.MaximizeBox = false;
            var sm = GetSystemMenu(Handle, false);
            EnableMenuItem(sm, SC_CLOSE, MF_BYCOMMAND | MF_DISABLED);
            zkfp2.Init();
            fp.InitEngine();
            int ret = zkfp.ZKFP_ERR_OK;
            if (IntPtr.Zero == (mDevHandle = zkfp2.OpenDevice(0)))
            {
                MessageBox.Show("OpenDevice fail");
                return;
            }
            if (IntPtr.Zero == (mDBHandle = zkfp2.DBInit()))
            {
                MessageBox.Show("Init DB fail");
                zkfp2.CloseDevice(mDevHandle);
                mDevHandle = IntPtr.Zero;
                return;
            }
           
            RegisterCount = 0;
            cbRegTmp = 0;
            iFid = 1;
            for (int i = 0; i < 3; i++)
            {
                RegTmps[i] = new byte[2048];
            }
            byte[] paramValue = new byte[4];
            int size = 4;
            zkfp2.GetParameters(mDevHandle, 1, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpWidth);

            size = 4;
            zkfp2.GetParameters(mDevHandle, 2, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpHeight);

            FPBuffer = new byte[mfpWidth * mfpHeight];

            size = 4;
            zkfp2.GetParameters(mDevHandle, 3, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpDpi);

           

            Thread captureThread = new Thread(new ThreadStart(DoCapture));
            captureThread.IsBackground = true;
            captureThread.Start();
            bIsTimeToDie = false;

            to_ram();

            Process[] p = Process.GetProcesses();
            foreach (Process ps in p)
            {
                string s = ps.ProcessName;
                s = s.ToLower();
                if (s.CompareTo("finger_voice") == 0) { ps.Kill(); }
               
            }
           
            Process pp = Process.Start(@"C:\report\fdb\Debug\finger_voice.exe");
            Thread.Sleep(500);
            pp.WaitForInputIdle();
            SetParent(pp.MainWindowHandle, this.Handle);
          
        }

        private void DoCapture()
        {
            while (!bIsTimeToDie)
            {
                cbCapTmp = 2048;
                int ret = zkfp2.AcquireFingerprint(mDevHandle, FPBuffer, CapTmp, ref cbCapTmp);
                if (ret == zkfp.ZKFP_ERR_OK)
                {
                    SendMessage(FormHandle, MESSAGE_CAPTURED_OK, IntPtr.Zero, IntPtr.Zero);
                }
               // Thread.Sleep(200);

            }

        }
        protected override void DefWndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case MESSAGE_CAPTURED_OK:
                    {
                        MemoryStream ms = new MemoryStream();
                        BitmapFormat.GetBitmap(FPBuffer, mfpWidth, mfpHeight, ref ms);
                        Bitmap bmp = new Bitmap(ms);
                        this.picFPImg.Image = bmp;
                        //  btnOutput.PerformClick();
                        //  btMatch.PerformClick();
                      //  textBox2.Text = "1";
                       


                        String strShow = zkfp2.BlobToBase64(CapTmp, cbCapTmp);
                      
                        txtTemplate1 = strShow;
                        match_finger();
                        if (IsRegister)
                        {
                            int ret = zkfp.ZKFP_ERR_OK;
                            int fid = 0, score = 0;
                            ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                            if (zkfp.ZKFP_ERR_OK == ret)
                            {
                               
                                return;
                            }

                            if (RegisterCount > 0 && zkfp2.DBMatch(mDBHandle, CapTmp, RegTmps[RegisterCount - 1]) <= 0)
                            {
                               
                                return;
                            }

                            Array.Copy(CapTmp, RegTmps[RegisterCount], cbCapTmp);
                            String strBase64 = zkfp2.BlobToBase64(CapTmp, cbCapTmp);
                            byte[] blob = zkfp2.Base64ToBlob(strBase64);
                            RegisterCount++;
                            if (RegisterCount >= REGISTER_FINGER_COUNT)
                            {
                                RegisterCount = 0;
                                if (zkfp.ZKFP_ERR_OK == (ret = zkfp2.DBMerge(mDBHandle, RegTmps[0], RegTmps[1], RegTmps[2], RegTmp, ref cbRegTmp)) &&
                                       zkfp.ZKFP_ERR_OK == (ret = zkfp2.DBAdd(mDBHandle, iFid, RegTmp)))
                                {
                                    iFid++;
                                  
                                }
                                else
                                {
                                   
                                }
                                IsRegister = false;
                                return;
                            }
                            else
                            {
                                
                            }
                        }
                        else
                        {
                            if (cbRegTmp <= 0)
                            {
                               
                                return;
                            }
                            if (bIdentify)
                            {
                                int ret = zkfp.ZKFP_ERR_OK;
                                int fid = 0, score = 0;
                                ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                                if (zkfp.ZKFP_ERR_OK == ret)
                                {
                                    
                                    return;
                                }
                                else
                                {
                                    
                                    return;
                                }
                            }
                            else
                            {
                                int ret = zkfp2.DBMatch(mDBHandle, CapTmp, RegTmp);
                                if (0 < ret)
                                {
                                   
                                    return;
                                }
                                else
                                {
                                    
                                    return;
                                }
                            }
                        }
                    }
                    break;

                default:
                    base.DefWndProc(ref m);
                    break;
            }

        }

        
        private void Form1_Load(object sender, EventArgs e)
        {
            FormHandle = this.Handle;
        }

      

        void match_finger()
        {
            try {
           

            for (int i = 0; i <= 10000;i++ )
            {

                int score;


                String strBase64 = answer[1,i];

                byte[] blob1 = Convert.FromBase64String(txtTemplate1.Trim());
                byte[] blob2 = Convert.FromBase64String(strBase64.Trim());

                score = zkfp2.DBMatch(mDBHandle, blob1, blob2);



                if (score > fp_m_p && button1.Enabled == true)
                {

                    try
                    {
                        
                        OracleConnection conn = new OracleConnection(Properties.Settings.Default.con_str);
                        conn.Open();
                        OracleCommand cmd = conn.CreateCommand();
                        cmd.CommandText = "insert into TEMP_FINGER(user_id,sl) values ('" + answer[0, i].ToLower().Replace(".bmp", "") + "',s1.nextval)";                        
                        cmd.ExecuteNonQuery();
                        conn.Close();
                        fp.ControlSensor(12, 1);
                        fp.ControlSensor(13, 1);
                        Thread.Sleep(200);
                        fp.ControlSensor(13, 0);
                        fp.ControlSensor(12, 0);
                    }
                    catch { }
                    break;

                }
                else if (score > fp_m_p && button1.Enabled == false)
                {
                    try
                    {
                       
                        fp.ControlSensor(12, 1);
                        fp.ControlSensor(13, 1);
                        Thread.Sleep(200);
                        fp.ControlSensor(13, 0);
                        fp.ControlSensor(12, 0);
                    }
                    catch { }
                    break;
                }


            }
            }
            catch { }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if(textBox2.Text=="12334")
            {



                string fileName = @"C:\report\fdb\fp\"+textBox1.Text+".bmp";
                if (fileName != "" && fileName != null && picFPImg.Image != null)
                {
                    //http://www.wischik.com/lu/programmer/1bpp.html
                    Bitmap bmp = new Bitmap(picFPImg.Image.Width, picFPImg.Image.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.DrawImage(picFPImg.Image, 0, 0, bmp.Width, bmp.Height);

                    }
                    Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                    System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, bmp.PixelFormat);
                    IntPtr ptr = bmpData.Scan0;
                    int bytes = bmpData.Stride * bmpData.Height;
                    byte[] rgbValues = new byte[bytes];
                    System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);
                    Rectangle rect2 = new Rectangle(0, 0, bmp.Width, bmp.Height);

                    Bitmap bit = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
                    System.Drawing.Imaging.BitmapData bmpData2 = bit.LockBits(rect2, System.Drawing.Imaging.ImageLockMode.ReadWrite, bit.PixelFormat);
                    IntPtr ptr2 = bmpData2.Scan0;
                    int bytes2 = bmpData2.Stride * bmpData2.Height;
                    byte[] rgbValues2 = new byte[bytes2];
                    System.Runtime.InteropServices.Marshal.Copy(ptr2, rgbValues2, 0, bytes2);
                    double colorTemp = 0;
                    for (int i = 0; i < bmpData.Height; i++)
                    {
                        for (int j = 0; j < bmpData.Width * 3; j += 3)
                        {
                            colorTemp = rgbValues[i * bmpData.Stride + j + 2] * 0.299 + rgbValues[i * bmpData.Stride + j + 1] * 0.578 + rgbValues[i * bmpData.Stride + j] * 0.114;
                            rgbValues2[i * bmpData2.Stride + j / 3] = (byte)colorTemp;
                        }
                    }
                    System.Runtime.InteropServices.Marshal.Copy(rgbValues2, 0, ptr2, bytes2);
                    bmp.UnlockBits(bmpData);
                    ColorPalette tempPalette;
                    {
                        using (Bitmap tempBmp = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format8bppIndexed))
                        {
                            tempPalette = tempBmp.Palette;
                        }
                        for (int i = 0; i < 256; i++)
                        {
                            tempPalette.Entries[i] = Color.FromArgb(i, i, i);
                        }
                        bit.Palette = tempPalette;
                    }
                    bit.UnlockBits(bmpData2);

                    bit.Save(fileName, picFPImg.Image.RawFormat);

                    bit.Dispose();
                }
                to_ram();
            }
        }

        private void textBox2_Click(object sender, EventArgs e)
        {
            textBox2.Clear();
        }

        private void textBox1_Click(object sender, EventArgs e)
        {
            textBox1.Clear();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            button1.Enabled = false;
            button3.Enabled = true;
            textBox1.Visible = true;
            textBox2.Visible = true;
            button2.Visible = true;
            button4.Visible = true;
            textBox1.Clear();
            textBox2.Clear();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            button1.Enabled = true;
            button3.Enabled = false;
            textBox1.Visible = false;
            textBox2.Visible = false;
            button2.Visible = false;
            button4.Visible = false;

        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (textBox2.Text == "12334")
            {
                try
                {
                    File.Delete(@"C:\report\fdb\fp\" + textBox1.Text + ".bmp");
                    to_ram();
                }
                catch { }
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
        }
       
       
    }
}
