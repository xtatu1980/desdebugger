﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;


namespace desdebugger
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        [DllImport("arm-disasm.dll")]
        static extern void Disasm(uint adr, uint ins, System.Text.StringBuilder str);
        [DllImport("arm-disasm.dll")]
        static extern void DisasmThumb(uint adr, uint ins, System.Text.StringBuilder str);

        private System.Net.Sockets.TcpClient client;
        private uint memoryAdr;
        private int insSize = 0;
        private uint[] registers;

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void buttonLaunch_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("desmume.exe", "--arm9gdb 1234");
        }

        private void buttonConnect_Click(object sender, EventArgs e)
        {
            client = new System.Net.Sockets.TcpClient("localhost", 1234);
            UpdateRegisters();
            GotoWithUpdate(0x02000000);
        }

        private void UpdateDisasm()
        {
            bool thumb = radioButtonThumb.Checked;
            if (insSize != 300)
            {
                insSize = 300;
                listBoxDisasm.Items.Clear();
                for (var i = 0; i < insSize; i++)
                {
                    listBoxDisasm.Items.Add("");
                }
            }
            var memory = thumb ? GetMemory16(memoryAdr, insSize) : GetMemory32(memoryAdr, insSize);
            
            for (var i = 0; i < memory.Length; i++)
            {
                var buf = new StringBuilder(256);
                var a = (uint)(memoryAdr + i * (thumb ? 2 : 4));
                if (thumb)
                {
                    DisasmThumb(a, memory[i], buf);
                }
                else
                {
                    Disasm(a, memory[i], buf);
                }
                var str = String.Format("{0:x8} ", a) + buf.ToString().ToLower();
                var match = System.Text.RegularExpressions.Regex.Match(str, @"\[pc, #([0-9a-f]+)\]");
                if (match.Success)
                {
                    var ofs = Convert.ToInt32(match.Groups[1].Value, 16);
                    if (thumb && ((i + 2) & ~1) + ofs / 2 + 1 < memory.Length)
                    {
                        str = str.Substring(0, match.Index) + String.Format("#{0:x8}", memory[((i + 2) & ~1) + ofs / 2] | memory[((i + 2) & ~1) + ofs / 2 + 1] << 16);
                    }
                }
                listBoxDisasm.Items[i] = str;
            }
        }

        private void GotoWithUpdate(uint adr)
        {
            var offset = -150;
            bool thumb = radioButtonThumb.Checked;
            memoryAdr = (uint)(adr + offset * (thumb ? 2 : 4));
            UpdateDisasm();
            listBoxDisasm.SelectedIndex = -offset + 20;
            listBoxDisasm.SelectedIndex = -offset;
        }

        private void Goto(uint adr)
        {
            bool thumb = radioButtonThumb.Checked;
            if (memoryAdr <= adr && adr < memoryAdr + insSize * 2)
            {

            }
            else
            {
                GotoWithUpdate(adr);
            }
            listBoxDisasm.SelectedIndex = (int)(adr - memoryAdr) / (thumb ? 2 : 4);
        }

        private void UpdateRegisters()
        {
            var reg = GetRegisters();
            registers = reg;
            listViewReg.Items.Clear();
            for (var i = 0; i < reg.Length; i++)
            {
                string[] item = { Convert.ToString(i), String.Format("{0:x8}", reg[i]) };
                listViewReg.Items.Add(new ListViewItem(item));
            }
        }

        private uint[] GetMemory16(uint adr, int size)
        {
            var res = Interact(String.Format("m{0:x8},{1:X}", adr, size * 2));
            var memory = new List<uint>();

            if (res[0] == 'E')
            {
                for (int i = 0; i < size; i++)
                {
                    memory.Add(0);
                }
            }
            else
            {
                for (int i = 0; i < size; i++)
                {
                    var str = res.Substring(i * 4, 4);
                    memory.Add(Convert.ToUInt32(str.Substring(2, 2) + str.Substring(0, 2), 16));
                }
            }
            return memory.ToArray();
        }

        private uint[] GetMemory32(uint adr, int size)
        {
            var res = Interact(String.Format("m{0:X8},{1:X}", adr, size * 4));
            var memory = new List<uint>();
            if (res[0] == 'E')
            {
                for (int i = 0; i < size; i++)
                {
                    memory.Add(0);
                }
            }
            else
            {
                for (int i = 0; i < res.Length / 8; i++)
                {
                    var str = res.Substring(i * 8, 8);
                    memory.Add(Convert.ToUInt32(str.Substring(6, 2) + str.Substring(4, 2) + str.Substring(2, 2) + str.Substring(0, 2), 16));
                }
            }
            return memory.ToArray();
        }

        private uint[] GetRegisters()
        {
            var registers = new List<uint>();
            string res = Interact("g");
            for (int i = 0; i < res.Length / 8; i ++)
            {
                var str = res.Substring(i * 8, 8);
                registers.Add(Convert.ToUInt32(str.Substring(6, 2) + str.Substring(4, 2) + str.Substring(2, 2) + str.Substring(0, 2), 16));
            }
            return registers.ToArray();
        }

        private string Interact(string request)
        {
            Console.WriteLine(request);
            var stream = client.GetStream();
            var bytes = System.Text.Encoding.UTF8.GetBytes("$" + request + "#" + String.Format("{0:X2}", Checksum(request)));
            stream.Write(bytes, 0, bytes.Length);
            var retBytes = new List<byte>();
            int c;
            stream.ReadByte();
            stream.ReadByte();
            while ((c = stream.ReadByte()) != Convert.ToByte('#'))
            {
                retBytes.Add((byte)c);
            }
            stream.ReadByte();
            stream.ReadByte();
            stream.WriteByte(Convert.ToByte('+'));
            var response = System.Text.Encoding.UTF8.GetString(retBytes.ToArray());
            Console.WriteLine(response);
            return response;
        }

        private int Checksum(string str)
        {
            var chars = str.ToCharArray();
            uint sum = 0;
            foreach (char c in chars)
            {
                sum += c;
            }
            return (int)(sum % 256);
        }

        private void buttonContinue_Click(object sender, EventArgs e)
        {
            Interact("c");
            UpdateRegisters();
            Goto(registers[15]);
        }

        private void buttonStep_Click(object sender, EventArgs e)
        {
            Interact("s");
            UpdateRegisters();
            Goto(registers[15]);
        }

        private void buttonBp_click(object sender, EventArgs e)
        {
            Interact(String.Format("Z0,{0:x8},4", Convert.ToUInt32(textBoxBp.Text, 16)));
        }

        private void buttonGoto_Click(object sender, EventArgs e)
        {
            GotoWithUpdate(Convert.ToUInt32(textBoxGoto.Text, 16));
        }

        private void radioButtonARM_CheckedChanged(object sender, EventArgs e)
        {
            UpdateDisasm();
        }

        private void radioButtonThumb_CheckedChanged(object sender, EventArgs e)
        {
            UpdateDisasm();
        }
    }
}
