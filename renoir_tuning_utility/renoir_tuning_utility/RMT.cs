﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using RyzenSmu;
using PowerSettings;
using System.Text.RegularExpressions;
using System.Reflection;
using RyzenSMUBackend;

namespace renoir_tuning_utility
{
    public partial class RMT : Form
    {
        [DllImport("inpoutx64.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysLong(UIntPtr memAddress, out uint Data);



        [DllImport("inpoutx64.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsInpOutDriverOpen();
        
        private static float ReadFloat(uint Address, uint Offset)
        {
            uint Data = 0;
            GetPhysLong((UIntPtr)(Address + Offset), out Data);

            byte[] bytes = new byte[4];
            bytes = BitConverter.GetBytes(Data);

            float PmData = BitConverter.ToSingle(bytes, 0);
            //Console.WriteLine($"0x{Address + Offset * 4,8:X8} | {PmData:F}");
            return PmData;
        }

        private static UInt32 ReadUInt32(uint Address, uint Offset)
        {
            uint Data = 0;
            GetPhysLong((UIntPtr)(Address + Offset), out Data);

            byte[] bytes = new byte[4];
            bytes = BitConverter.GetBytes(Data);

            UInt32 PmData = BitConverter.ToUInt32(bytes, 0);
            //Console.WriteLine($"0x{Address + Offset * 4,8:X8} | {PmData:F}");
            return PmData;
        }


        uint Msg;
        uint[] Args;
        uint Address;
        Smu RyzenAccess;
        bool EnableDebug;
        bool LoadValues;
        bool DumpTable;
        UInt32 PMTableVersion;
        Thread MonitoringThread;
        
        PerformanceCounter cpuCounter;
        int CbResult;

        List<float> PowerDraw;

        public RMT()
        {
            EnableDebug = false;
            LoadValues = true;
            DumpTable = true;

            InitializeComponent();
            checkFastLimit.Checked = true;
            checkSlowLimit.Checked = true;
            checkStapmLimit.Checked = true;
            checkSlowTime.Checked = true;
            checkStapmTime.Checked = true;
            checkTctlTemp.Checked = true;
            checkCurrentLimit.Checked = true;
            checkMaxCurrentLimit.Checked = true;
            checkSttLimit.Checked = true;

            checkBoxGfx.Checked = false;
            upDownGfx.Enabled = false;

            PowerSetting CurrentSetting;

            Families.SetFam();


            labelRenoirMobileTuning.Text = "RMT v1.3.0 Beta | " + (new Smu(false)).GetCpuName();



            /*
            //loading inpoutx64
            var outPutDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
            var iconPath = Path.Combine(outPutDirectory, "inpoutx64.sys");
            string icon_path = new Uri(iconPath).LocalPath;

            MessageBox.Show(icon_path);
            Assembly.UnsafeLoadFrom("inpoutx64.sys");
            //Assembly.LoadFrom(icon_path);

            */




            int time = 0;
            while(!IsInpOutDriverOpen() && time < 100)
            {
                time++;
                Thread.Sleep(1);
            }
            if(time == 100)
            {
                MessageBox.Show("Failed to load InpOutx64 driver.\nPlease check that the application is running with the right privledges");
            }


            if(LoadValues)
            {
                Args = new uint[6];
                RyzenAccess = new Smu(EnableDebug);
                RyzenAccess.Initialize();
                //labelRenoirMobileTuning.Text += " - " + RyzenAccess.GetCpuName();
                
                if(RyzenAccess.SendPsmu(0x6,ref Args) == Smu.Status.OK)
                {
                    PMTableVersion = Args[0];
                }

                if(RyzenAccess.SendPsmu(0x66, ref Args) == Smu.Status.OK)
                {
                    //Set Address and reset Args[]
                    Address = Args[0];
                    Args[0] = 0;

                    //Dump the Power Monitoring Table
                    RyzenAccess.SendPsmu(0x65, ref Args);
                    //Sleep so that the SMU has time to dump the PM Table properly.
                    Thread.Sleep(100);

                    if(DumpTable)
                    {
                        for (int i = 0; i <= 1000 && ReadFloat(Address, 0x0) == 0f; i++)
                        {
                            if(i == 999)
                                MessageBox.Show("Unable To start InpOutX64. Check the application permissions and see if anything is stopping InpOutX64 from working.");
                            Thread.Sleep(1);
                        }

                            UInt32[] SmuVersionArgs = new UInt32[6];
                        string[] TableDump = new string[705];
                        Args[0] = 0;
                        RyzenAccess.SendMp1(0x2, ref Args);
                        for (int i = 0; i < 6; i++)
                        {
                            SmuVersionArgs[i] = Args[i];
                        }
                        Thread.Sleep(1);


                        TableDump.Initialize();
                        TableDump[0] = (labelRenoirMobileTuning.Text);
                        String SmuVersion = "";//$"{SmuVersionArgs[0]:X8}".Substring(0,2) + ".";
                        SmuVersion += $"{SmuVersionArgs[0]:X8}".Substring(2, 2) + ".";
                        SmuVersion += $"{SmuVersionArgs[0]:X8}".Substring(4, 2) + ".";
                        SmuVersion += $"{SmuVersionArgs[0]:X8}".Substring(6, 2);
                        TableDump[1] = ($"SMU Version: {SmuVersionArgs[0]:X8}");
                        TableDump[2] = ($"SMU Version: " + SmuVersion);
                        TableDump[3] = ($"PMTableBaseAddress: 0x{Address:X8}");
                        float CurrentValue = 0.0F;
                        bool OnlyZero = true;
                        for (UInt32 i = 0; i <= 700; i++)
                        {
                            CurrentValue = Smu.ReadFloat(Address, i);
                            if(OnlyZero && CurrentValue != 0.0F )
                            {
                                OnlyZero = false;
                            }
                            TableDump[4+i] = $"0x{i:X4}\t{CurrentValue:F4}";
                        }
                        File.WriteAllLines("PMTableDump.log", TableDump);
                        if(OnlyZero)
                        {
                            MessageBox.Show("Error Dumping the PM Table\nThe PM Table only contains zeros!", "Power Monitoring Table Dump:");

                        }
                        else
                        {
                            //MessageBox.Show("Successfully Dumped the PM Table", "Power Monitoring Table Dump:");

                        }
                    }

                    //Loading of table info with static offsets

                    //Stapm Limit
                    try
                    {
                        upDownStapmLimit.Value = (decimal)(Smu.ReadFloat(Address, 0x0));
                        upDownSttLimit.Value = upDownStapmLimit.Value;
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "StapmLimit Load Error");
                        upDownStapmLimit.Value = 15;
                    }

                    //Fast Limit
                    try
                    {
                        upDownFastLimit.Value = (decimal)(Smu.ReadFloat(Address, 0x2));
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "FastLimit Load Error");
                        upDownFastLimit.Value = 21;
                    }

                    //Slow Limit
                    try
                    {
                        upDownSlowLimit.Value = (decimal)(Smu.ReadFloat(Address, 0x4));
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "SlowLimit Load Error");
                        upDownSlowLimit.Value = 18;
                    }

                    //Current Limit
                    try
                    {
                        upDownCurrentLimit.Value = (decimal)(Smu.ReadFloat(Address, 0x8));
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "CurrentLimit Load Error");
                        upDownCurrentLimit.Value = 25;
                    }

                    //TctlTemp
                    try
                    {
                        upDownTctlTemp.Value = (decimal)(Smu.ReadFloat(Address, 0x10));
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "TctlTemp Load Error");
                        upDownTctlTemp.Value = 80;
                    }

                    //Max Current Limit
                    try
                    {
                        upDownMaxCurrentLimit.Value = (decimal)(Smu.ReadFloat(Address, 0xC));
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "MaxCurrentLimit Load Error");
                        upDownMaxCurrentLimit.Value = 50;
                    }

                    //Skin Temperature Limit
                    try
                    {
                        upDownSttLimit.Value = (decimal)(Smu.ReadFloat(Address, 0x16));
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "SstLimit Load Error");
                        upDownMaxCurrentLimit.Value = 50;
                    }
                    if(upDownSttLimit.Value == 0)
                    {
                        checkSttLimit.Checked = false;
                        upDownSttLimit.Enabled = false;
                    }

                    //MessageBox.Show($"PM Table Version 0x{PMTableVersion:X8}");

                    switch (PMTableVersion)
                    {
                        case 0x00370005:
                            //A15 at time of testing
                            PMTableVersion = 0x00370005;

                            //Stapm Time
                            try
                            {
                                upDownStapmTime.Value = (decimal)(Smu.ReadFloat(Address, 0x227));
                            }
                            catch (Exception e)
                            {
                                MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "StapmTime Load Error");
                                upDownStapmTime.Value = 300;
                            }

                            //Slow Time
                            try
                            {

                                upDownSlowTime.Value = (decimal)(Smu.ReadFloat(Address, 0x228));
                            }
                            catch (Exception e)
                            {
                                MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "SlowTime Load Error");
                                upDownSlowTime.Value = 3;
                            }
                            break;

                        case 0x00370003:
                        case 0x00370004:



                            //Update Current Settings if user just wants those settings re-applied
                            UpdateCurrentSettings();

                            //G14 At time of testing
                            PMTableVersion = 0x00370004;

                            //Stapm Time
                            try
                            {
                                upDownStapmTime.Value = (decimal)(Smu.ReadFloat(Address, 0x220));
                            }
                            catch (Exception e)
                            {
                                MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "StapmTime Load Error");
                                upDownStapmTime.Value = 300;
                            }

                            //Slow Time
                            try
                            {
                                upDownSlowTime.Value = (decimal)(Smu.ReadFloat(Address, 0x221));
                            }
                            catch (Exception e)
                            {
                                MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/unsupported Power Monitoring Table version\n" + e.ToString(), "SlowTime Load Error");
                                upDownSlowTime.Value = 3;
                            }

                            //Update Current Settings if user just wants those settings re-applied
                            UpdateCurrentSettings();
                            break;

                        default:
                            MessageBox.Show("Please send your PMTableDump.log to the devs!\nMost Likely a new/ unsupported Power Monitoring Table version", "Table Version Error");
                            break;
                    }
                    
                }
            }
        }

        public bool CheckSettings(float FastLimit, float SlowLimit, float StapmLimit, float SlowTime, float StapmTime, float TctlTemp, float CurrentLimit, float MaxCurrentLimit, object sender, EventArgs e)
        {
            bool fl = FastLimit != (float)upDownFastLimit.Value && checkFastLimit.Checked;
            bool sl = SlowLimit != (float)upDownSlowLimit.Value && checkSlowLimit.Checked;
            bool stl = StapmLimit != (float)upDownStapmLimit.Value && checkStapmLimit.Checked;
            bool st = SlowTime != (float)upDownSlowTime.Value && checkSlowTime.Checked;
            bool stt = StapmTime != (float)upDownStapmTime.Value && checkStapmTime.Checked;
            bool tctl = TctlTemp != (float)upDownTctlTemp.Value && checkTctlTemp.Checked;
            bool cl = CurrentLimit != (float)upDownCurrentLimit.Value && checkCurrentLimit.Checked;
            bool mcl = MaxCurrentLimit != (float)upDownMaxCurrentLimit.Value && checkMaxCurrentLimit.Checked;


            if(fl || sl || stl || st || stt || tctl || cl || mcl)
            {
                ApplySettings_Click_1(sender, e);
                return true;
            }

            return false;
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            
            
        }


        private void checkTctlTemp_CheckedChanged(object sender, EventArgs e)
        {
            upDownTctlTemp.Enabled = checkTctlTemp.Checked;
        }

        private void checkStapmLimit_CheckedChanged(object sender, EventArgs e)
        {
            upDownStapmLimit.Enabled = checkStapmLimit.Checked;
        }

        private void checkSlowTime_CheckedChanged(object sender, EventArgs e)
        {
            upDownSlowTime.Enabled = checkSlowTime.Checked;
        }

        private void checkStapmTime_CheckedChanged(object sender, EventArgs e)
        {
            upDownStapmTime.Enabled = checkStapmTime.Checked;
        }

        private void checkCurrentLimit_CheckedChanged(object sender, EventArgs e)
        {
            upDownCurrentLimit.Enabled = checkCurrentLimit.Checked;
        }


        private void ApplySettings_Click_1(object sender, EventArgs e)
        {
            labelLog.Text = "Log:\n";
            labelLog.Update();
            RyzenAccess = new Smu(false);

            RyzenAccess.Initialize();

            //String exe = Directory.GetCurrentDirectory() + "\\smu-tool\\smu-tool.exe";
            
            int i = 0;
            Smu.Status[] Statuses = new Smu.Status[10];
            Args = new uint[6];

            if(checkStapmLimit.Checked)
            {
                //Set Msg and Args
                Msg = 0x14;
                Args[0] = (uint)Convert.ToUInt32(upDownStapmLimit.Value * 1000);

                SendCommand.set_stapm_limit((uint)Convert.ToUInt32(upDownStapmLimit.Value * 1000));

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                //labelLog.Text += $"STAPM Power Limit: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }

            if(checkFastLimit.Checked)
            {
                //Set Msg and Args
                Msg = 0x15;
                Args[0] = (uint)Convert.ToUInt32(upDownFastLimit.Value * 1000);

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                SendCommand.set_fast_limit((uint)Convert.ToUInt32(upDownFastLimit.Value * 1000));


                //labelLog.Text += $"Fast Power Limit: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }

            if(checkSlowLimit.Checked)
            {
                //Set Msg and Args
                Msg = 0x16;
                Args[0] = (uint)Convert.ToUInt32(upDownSlowLimit.Value * 1000);

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                SendCommand.set_slow_limit((uint)Convert.ToUInt32(upDownSlowLimit.Value * 1000));

                //labelLog.Text += $"Slow Power Limit: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }

            if(checkSlowTime.Checked)
            {
                //Set Msg and Args
                Msg = 0x17;
                Args[0] = (uint)Convert.ToUInt32(upDownSlowTime.Value);

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                SendCommand.set_slow_time((uint)Convert.ToUInt32(upDownSlowTime.Value * 1000));

                //labelLog.Text += $"Fast Boost Duration: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }

            if(checkStapmTime.Checked)
            {
                //Set Msg and Args
                Msg = 0x18;
                Args[0] = (uint)Convert.ToUInt32(upDownStapmTime.Value);

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                SendCommand.set_stapm_time((uint)Convert.ToUInt32(upDownStapmTime.Value * 1000));

                //labelLog.Text += $"Slow Boost Duration: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }

            if(checkTctlTemp.Checked)
            {
                //Set Msg and Args
                Msg = 0x19;
                Args[0] = (uint)Convert.ToUInt32(upDownTctlTemp.Value);

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                SendCommand.set_tctl_temp((uint)Convert.ToUInt32(upDownTctlTemp.Value * 1000));

                //labelLog.Text += $"Temperature Limit: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }

            if(checkCurrentLimit.Checked)
            {
                //Set Msg and Args
                Msg = 0x1A;
                Args[0] = (uint)Convert.ToUInt32(upDownCurrentLimit.Value * 1000);

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                SendCommand.set_vrm_current((uint)Convert.ToUInt32(upDownCurrentLimit.Value * 1000));

                //labelLog.Text += $"Current Limit: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }
            if(checkMaxCurrentLimit.Checked)
            {
                //Set Msg and Args
                Msg = 0x1C;
                Args[0] = (uint)Convert.ToUInt32(upDownMaxCurrentLimit.Value * 1000);

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                SendCommand.set_vrmmax_current((uint)Convert.ToUInt32(upDownMaxCurrentLimit.Value * 1000));

                //labelLog.Text += $"Maximum Current Limit: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }
            if(checkSttLimit.Checked)
            {
                Msg = 0x38;
                Args[0] = (uint)Convert.ToUInt32(upDownSttLimit.Value * 256);

                ////Send msg
                //Statuses[i++] = RyzenAccess.SendMp1(Msg, ref Args);

                SendCommand.set_apu_skin_temp_limit((uint)Convert.ToUInt32(upDownSttLimit.Value * 256));

                //labelLog.Text += $"Skin Temperature Limit: " + Statuses[i-1].ToString() + '\n';
                labelLog.Update();
            }
            if(checkBoxGfx.Checked)
            {
                Msg = 0x89;
                Args[0] = (uint)Convert.ToUInt32(upDownGfx.Value);

                //Send msg
                //Statuses[i++] = RyzenAccess.SendPsmu(Msg, ref Args);

                SendCommand.set_gfx_clk((uint)Convert.ToUInt32(upDownGfx.Value));

                //labelLog.Text += $"GFX Clockspeed: " + Statuses[i - 1].ToString() + '\n';
                labelLog.Update();
            }

            bool ApplyError = false;

            //for(int j = 0; j < i; j++)
            //{
            //    //labelLog.Text += $"{j:D}-Status: " + Statuses[j].ToString() + '\n';
            //    if(Statuses[j] != Smu.Status.OK)
            //    {
            //        ApplyError = true;
            //    }
            //}

            if(ApplyError)
            {
                labelLog.Text += "\nError while appling settings.\n";
            }
            else
            {
                labelLog.Text += "\nSettings applied successfully.\n";
            }

            //Kill off our access to the SMU so that other parts of the program may use it.
            RyzenAccess.Deinitialize();

        }

        //Updates the CurrentSettings so that changes will change when smart re-apply is being used
        private void UpdateCurrentSettings()
        {
            PowerSetting CurrentSetting = new PowerSetting(PMTableVersion);
            CurrentSetting.Name = "Current Settings";
            CurrentSetting.SmartReapply = checkSmartReapply.Checked;
            CurrentSetting.StapmLimit = Convert.ToUInt32(upDownStapmLimit.Value * 1000);
            CurrentSetting.FastLimit = Convert.ToUInt32(upDownFastLimit.Value * 1000);
            CurrentSetting.SlowLimit = Convert.ToUInt32(upDownSlowLimit.Value * 1000);
            CurrentSetting.SlowTime = Convert.ToUInt32(upDownSlowTime.Value);
            CurrentSetting.StapmTime = Convert.ToUInt32(upDownStapmTime.Value);
            CurrentSetting.TctlTemp = Convert.ToUInt32(upDownTctlTemp.Value);
            CurrentSetting.CurrentLimit = Convert.ToUInt32(upDownCurrentLimit.Value * 1000);
            CurrentSetting.MaxCurrentLimit = Convert.ToUInt32(upDownMaxCurrentLimit.Value * 1000);
            CurrentSetting.SstLimit = Convert.ToUInt32(upDownSttLimit.Value * 256);
            
            //File.Create("CurrentSettings.json
            File.WriteAllText("CurrentSettings.json", JsonConvert.SerializeObject(CurrentSetting));
        }

        private void checkMaxCurrentLimit_CheckedChanged(object sender, EventArgs e)
        {
            upDownMaxCurrentLimit.Enabled = checkMaxCurrentLimit.Checked;
        }

        private void upDownSlowLimit_ValueChanged(object sender, EventArgs e)
        {
            if(upDownFastLimit.Value < upDownSlowLimit.Value)
            {
                upDownFastLimit.Value = upDownSlowLimit.Value;
            }
            if(upDownSlowLimit.Value < upDownStapmLimit.Value)
            {
                upDownStapmLimit.Value = upDownSlowLimit.Value;
                upDownStapmLimit_ValueChanged(sender, e);
            }
        }

        private void upDownStapmLimit_ValueChanged(object sender, EventArgs e)
        {
            if(upDownFastLimit.Value < upDownStapmLimit.Value)
            {
                upDownFastLimit.Value = upDownStapmLimit.Value;
            }
            if(upDownSlowLimit.Value < upDownStapmLimit.Value)
            {
                upDownSlowLimit.Value = upDownStapmLimit.Value;
            }
        }

        private void upDownSlowTime_ValueChanged(object sender, EventArgs e)
        {
            /*if(Math.Round(upDownSlowTime.Value * 2) > upDownStapmTime.Value)
            {
                upDownStapmTime.Value = upDownSlowTime.Value * 2;
            }*/
        }

        private void upDownStapmTime_ValueChanged(object sender, EventArgs e)
        {
            /*if(Math.Round(upDownSlowTime.Value * 2) > upDownStapmTime.Value)
            {
                upDownSlowTime.Value = Math.Round(upDownStapmTime.Value / 2);
            }*/
        }

        private void upDownCurrentLimit_ValueChanged(object sender, EventArgs e)
        {
            if(upDownCurrentLimit.Value > upDownMaxCurrentLimit.Value)
            {
                upDownMaxCurrentLimit.Value = upDownCurrentLimit.Value;
            }
        }

        private void upDownMaxCurrentLimit_ValueChanged(object sender, EventArgs e)
        {
            if(upDownCurrentLimit.Value > upDownMaxCurrentLimit.Value)
            {
                upDownCurrentLimit.Value = upDownMaxCurrentLimit.Value;
            }
        }

        private void checkMinimizeToTray_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void notifyIconRMT_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
            notifyIconRMT.Visible = false;
        }

        private void monitoringTextBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void ShowSensors_Click_1(object sender, EventArgs e)
        {
            UpdateCurrentSettings();
            new Thread(() => new SystemMonitor().ShowDialog()).Start();
        }

        private void checkSlowLimit_CheckedChanged(object sender, EventArgs e)
        {
            upDownSlowLimit.Enabled = checkSlowLimit.Checked;
        }

        

        private void checkFastLimit_CheckedChanged(object sender, EventArgs e)
        {
            upDownFastLimit.Enabled = checkFastLimit.Checked;
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            if(upDownFastLimit.Value < upDownSlowLimit.Value)
            {
                upDownSlowLimit.Value = upDownFastLimit.Value;
            }
            if(upDownFastLimit.Value < upDownStapmLimit.Value)
            {
                upDownStapmLimit.Value = upDownFastLimit.Value;
                upDownStapmLimit_ValueChanged(sender, e);
            }
            
        }

        private void panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        private void buttonSaveSettings_Click(object sender, EventArgs e)
        {
            
            //CurrentSetting.GfxClk = Convert.ToUInt32(upDownGfxClk.Value);
            Stream myStream;
            SaveFileDialog saveFileDialog1 = new SaveFileDialog();

            saveFileDialog1.Filter = "Renoir Mobile Tuning Setting files (*.rmts)|*.rmts| All files (*.*)|*.*";
            saveFileDialog1.FilterIndex = 1;
            saveFileDialog1.RestoreDirectory = true;

            if(saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                if((myStream = saveFileDialog1.OpenFile()) != null)
                {
                    
                    //Create new PowerSetting
                    PowerSetting CurrentSetting = new PowerSetting(PMTableVersion);

                    string FileName = saveFileDialog1.FileName;
                    int NameStartIndex = FileName.LastIndexOf('\\') + 1;
                    
                    CurrentSetting.Name = FileName.Substring(NameStartIndex, FileName.Length - NameStartIndex - 5);

                    //Set PowerSetting variabes
                    CurrentSetting.StapmLimit = Convert.ToUInt32(upDownStapmLimit.Value * 1000);
                    CurrentSetting.FastLimit = Convert.ToUInt32(upDownFastLimit.Value * 1000);
                    CurrentSetting.SlowLimit = Convert.ToUInt32(upDownSlowLimit.Value * 1000);
                    CurrentSetting.SlowTime = Convert.ToUInt32(upDownSlowTime.Value);
                    CurrentSetting.StapmTime = Convert.ToUInt32(upDownStapmTime.Value);
                    CurrentSetting.TctlTemp = Convert.ToUInt32(upDownTctlTemp.Value);
                    CurrentSetting.CurrentLimit = Convert.ToUInt32(upDownCurrentLimit.Value * 1000);
                    CurrentSetting.MaxCurrentLimit = Convert.ToUInt32(upDownMaxCurrentLimit.Value * 1000);
                    CurrentSetting.SstLimit = Convert.ToUInt32(upDownSttLimit.Value * 256);


                    //Convert to string
                    string JsonString = JsonConvert.SerializeObject(CurrentSetting);
                    
                    //Write and Close 
                    myStream.Write(Encoding.UTF8.GetBytes(JsonString), 0, JsonString.Length);
                    myStream.Close();
                }
            }
            //File.WriteAllText("test.json", JsonConvert.SerializeObject(CurrentSetting));
            

        }

        private void buttonLoadSettings_Click(object sender, EventArgs e)
        {

            var FileContent = string.Empty;
            var FilePath = string.Empty;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                //openFileDialog.InitialDirectory = "c:\\";
                openFileDialog.Filter = "Renoir Mobile Tuning Setting files (*.rmts)|*.rmts| All files (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                if(openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    //Get the path of specified file
                    FilePath = openFileDialog.FileName;

                    //Read the contents of the file into a stream
                    var fileStream = openFileDialog.OpenFile();

                    using (StreamReader reader = new StreamReader(fileStream))
                    {
                        FileContent = reader.ReadToEnd();
                    }

                    PowerSetting CurrentSetting = JsonConvert.DeserializeObject<PowerSetting>(FileContent);


                    upDownFastLimit.Value = (decimal)((float)CurrentSetting.FastLimit / 1000F);
                    upDownSlowLimit.Value = (decimal)((float)CurrentSetting.SlowLimit / 1000F);
                    upDownStapmLimit.Value = (decimal)((float)CurrentSetting.StapmLimit / 1000F);
                    upDownSlowTime.Value = CurrentSetting.SlowTime;
                    upDownStapmTime.Value = CurrentSetting.StapmTime;
                    upDownTctlTemp.Value = CurrentSetting.TctlTemp;
                    upDownCurrentLimit.Value = (decimal)((float)CurrentSetting.CurrentLimit / 1000F);
                    upDownMaxCurrentLimit.Value = (decimal)((float)CurrentSetting.MaxCurrentLimit / 1000F);
                }
            }


            
            //upDownGfxClk.Value = CurrentSetting.GfxClk;
        }

        private void checkShowSensors_CheckedChanged(object sender, EventArgs e)
        {
            UpdateCurrentSettings();
            Thread.Sleep(10);
            if(checkShowSensors.Checked)
            {

                MonitoringThread = new Thread(() => new SystemMonitor().ShowDialog());
                MonitoringThread.Start();
            }
            else
            {
                MonitoringThread.Abort();
            }
                
        }

        private void checkSmartReapply_CheckedChanged(object sender, EventArgs e)
        {
            UpdateCurrentSettings();
            if(checkSmartReapply.Checked)
            {
                if(checkShowSensors.Checked)
                    checkShowSensors.Enabled = false;
                else
                {
                    checkShowSensors.Checked = true;
                    checkShowSensors.Enabled = false;
                }
            }
            else
            {
                checkShowSensors.Enabled = true;
            }

        }

        private void linkDiscord_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo("https://discord.gg/EBC3zRg");
            Process.Start(sInfo);
        }

        private void linkPayPal_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo("https://www.paypal.me/KeatonBlomquist");
            Process.Start(sInfo);
        }

        private void linkGitHub_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo("https://github.com/sbski/Renoir-Mobile-Tuning");
            Process.Start(sInfo);
        }

        private void linkTwitter_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo("https://twitter.com/KeatonBlomquist");
            Process.Start(sInfo);
        }

        private void linkCtr_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo("https://www.guru3d.com/files-details/clocktuner-for-ryzen-download.html");
            Process.Start(sInfo);
        }

        private void Presets_Click(object sender, EventArgs e)
        {

        }

        private void upDownSstLimit_ValueChanged(object sender, EventArgs e)
        {
            
        }

        private void checkSstLimit_CheckedChanged(object sender, EventArgs e)
        {
            upDownSttLimit.Enabled = checkSttLimit.Checked;
        }

        private void button1_Click(object sender, EventArgs e)
        {

            StartTuning(sender, e);
            //MessageBox.Show($"Start");


        }

        private async Task StartTuning(object sender, EventArgs e)
        {
            upDownFastLimit.Enabled = false;
            upDownSlowLimit.Enabled = false;
            upDownStapmLimit.Enabled = false;
            upDownSlowTime.Enabled = false;
            upDownStapmTime.Enabled = false;
            upDownTctlTemp.Enabled = false;
            upDownCurrentLimit.Enabled = false;
            upDownMaxCurrentLimit.Enabled = false;
            upDownSttLimit.Enabled = false;

            

            checkFastLimit.Enabled = false;
            checkSlowLimit.Enabled = false;
            checkStapmLimit.Enabled = false;
            checkSlowTime.Enabled = false;
            checkStapmTime.Enabled = false;
            checkTctlTemp.Enabled = false;
            checkCurrentLimit.Enabled = false;
            checkMaxCurrentLimit.Enabled = false;
            checkSttLimit.Enabled = false;


            ApplySettings.Enabled = false;
            buttonSaveSettings.Enabled = false;
            buttonLoadSettings.Enabled = false;
            checkSmartReapply.Enabled = false;

            upDownFastLimit.Value = 100;
            upDownSlowLimit.Value = 100;
            upDownStapmLimit.Value = 100;

            UInt32 CooldownTime = 90000;

            //remove power limits
            ApplySettings_Click_1(sender, e);


            PowerDraw = new List<float>();
            PowerDraw.Clear();

            int PowerDrawIndex = 0;
            RyzenAccess.Initialize();
            CbResult = 0;

            labelCinebenchValues.Text = "Runing first pass.";
            labelCinebenchValues.Update();

            int PowerScore = await CB20_func();

            //Set cooldown power limits
            upDownFastLimit.Value = 10;
            upDownFastLimit.Update();
            upDownSlowLimit.Value = upDownFastLimit.Value;
            upDownStapmLimit.Value = upDownFastLimit.Value;

            //Update settings
            ApplySettings_Click_1(sender, e);

            for (int i = 0; i < CooldownTime; i+=1000)
            {
                labelCinebenchValues.Text = $"In cooldown - {i:D}\n{PowerScore:D}";
                labelCinebenchValues.Update();
                await Task.Delay(1000);

            }

            //Boost power limit calcualtion
            upDownFastLimit.Value = (decimal)PowerDraw.Average();
            upDownFastLimit.Update();
            upDownSlowLimit.Value = (decimal)PowerDraw.Average();
            upDownStapmLimit.Value = (decimal)PowerDraw.Average();

            //Update settings
            ApplySettings_Click_1(sender, e);
            await Task.Delay(500);


            //Reset PowerDraw list
            PowerDraw = new List<float>();
            PowerDraw.Clear();

            labelCinebenchValues.Text = $"Runing second pass.\n{PowerScore:D}";
            labelCinebenchValues.Update();


            //Get TunedScore
            int TunedScore = await CB20_func();
            TunedScore = CbResult;

            for (int i = 0; i < 2000; i += 1000)
            {
                await Task.Delay(1000);
            }

            labelCinebenchValues.Text = $"Done.\n{PowerScore:D}\n" + $"{TunedScore:D}";
            labelCinebenchValues.Update();


            //MessageBox.Show($"{PowerScore:D}\n{TunedScore:D}");

            checkFastLimit_CheckedChanged(sender, e);
            checkSlowLimit_CheckedChanged(sender, e);
            checkStapmLimit_CheckedChanged(sender, e);
            checkSlowTime_CheckedChanged(sender, e);
            checkStapmTime_CheckedChanged(sender, e);
            checkTctlTemp_CheckedChanged(sender, e);
            checkCurrentLimit_CheckedChanged(sender, e);
            checkMaxCurrentLimit_CheckedChanged(sender, e);
            checkSstLimit_CheckedChanged(sender, e);

            checkFastLimit.Enabled = true;
            checkSlowLimit.Enabled = true;
            checkStapmLimit.Enabled = true;
            checkSlowTime.Enabled = true;
            checkStapmTime.Enabled = true;
            checkTctlTemp.Enabled = true;
            checkCurrentLimit.Enabled = true;
            checkMaxCurrentLimit.Enabled = true;
            checkSttLimit.Enabled = true;

            ApplySettings.Enabled = true;
            buttonSaveSettings.Enabled = true;
            buttonLoadSettings.Enabled = true;
            checkSmartReapply.Enabled = true;



        }

        public string getCurrentCpuUsage()
        {
            return cpuCounter.NextValue() + "%";
        }

        private async Task<int> CB20_func()
        {
            bool token = true;
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");


            string str = "g_CinebenchCpuXTest=true g_logFile=\"" + Directory.GetCurrentDirectory() + "\\CBlog.log\"";
            Process CB20process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    WorkingDirectory = Directory.GetCurrentDirectory() + "\\CB20",
                    FileName = "Cinebench.exe",
                    Verb = "runas",
                    Arguments = str ?? ""
                }
            };
            CB20process.Start();
            foreach (Process process in Process.GetProcessesByName("Cinebench"))
                process.PriorityClass = ProcessPriorityClass.AboveNormal;
            
            await System.Threading.Tasks.Task.Delay(5000);
            
            do
            {
                Args = new UInt32[6];
                float CpuUsage = cpuCounter.NextValue();
                if (CpuUsage < 65.0 && ((IEnumerable<Process>)Process.GetProcesses(".")).OrderBy<Process, int>((Func<Process, int>)(proc => proc.Id)).Count<Process>((Func<Process, bool>)(p => p.ProcessName.Contains("Cinebench"))) == 0)
                    token = false;
                if (CpuUsage > 90f)
                {
                    RyzenAccess.SendPsmu(0x65, ref Args);
                    PowerDraw.Add(Smu.ReadFloat(Address, 0x26));
                }
                //Test prints
                //labelCinebenchValues.Text = PowerDraw.Last().ToString();
                //labelCinebenchValues.Update();

                await System.Threading.Tasks.Task.Delay(1000);
            }
            while (token);
            await System.Threading.Tasks.Task.Delay(500);
            string CinebenchResults = "";

            labelCinebenchValues.Text = "";




            try
            {
                
                string input = File.ReadAllText("CBlog.log", Encoding.ASCII);
                string pattern = "CB ";
                foreach (Match match in Regex.Matches(input, pattern, RegexOptions.IgnoreCase))
                {
                    CbResult = (int)short.Parse(input.Substring(match.Index + pattern.Length, 4));
                    CinebenchResults += $"{CbResult:D}"+'\n';
                }
                //Test messagebox
                File.Delete("CBlog.log");
                await Task.Delay(500);




                return CbResult;
            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.ToString(), "Cinebench result error");
                return 0;
            }
        }

        private void checkBoxGfx_CheckedChanged(object sender, EventArgs e)
        {
            upDownGfx.Enabled = checkBoxGfx.Checked;
        }

        private void labelBclk_Click(object sender, EventArgs e)
        {

        }

        private void numericBclk_ValueChanged(object sender, EventArgs e)
        {
            RwMmioAmd MMIO = new RwMmioAmd();
            MMIO.SetBclk(Convert.ToDouble(numericBclk.Value));
            labelCDS.Text = Convert.ToDouble(numericBclk.Value).ToString() + "MHz";
            //labelCurBclk.Text = MMIO.GetBclk().ToString() + "MHz";
        }

        private void numericUpDownCDS_ValueChanged(object sender, EventArgs e)
        {

        }

        private void upDownGfx_ValueChanged(object sender, EventArgs e)
        {

        }
    }
}
