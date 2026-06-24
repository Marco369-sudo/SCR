namespace ScreenRecord
{
    partial class fMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            panel1 = new Panel();
            pictureBox1 = new PictureBox();
            panel2 = new Panel();
            panel3 = new Panel();
            listBox2 = new ListBox();
            listBox1 = new ListBox();
            panel4 = new Panel();
            checkBox1 = new CheckBox();
            bStop = new Button();
            bStart = new Button();
            panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            panel2.SuspendLayout();
            panel3.SuspendLayout();
            panel4.SuspendLayout();
            SuspendLayout();
            // 
            // panel1
            // 
            panel1.Controls.Add(pictureBox1);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(0, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(839, 299);
            panel1.TabIndex = 0;
            // 
            // pictureBox1
            // 
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(0, 0);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(839, 299);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // panel2
            // 
            panel2.Controls.Add(panel3);
            panel2.Controls.Add(panel4);
            panel2.Dock = DockStyle.Bottom;
            panel2.Location = new Point(0, 299);
            panel2.Name = "panel2";
            panel2.Size = new Size(839, 209);
            panel2.TabIndex = 1;
            // 
            // panel3
            // 
            panel3.Controls.Add(listBox2);
            panel3.Controls.Add(listBox1);
            panel3.Dock = DockStyle.Fill;
            panel3.Location = new Point(0, 0);
            panel3.Name = "panel3";
            panel3.Size = new Size(684, 209);
            panel3.TabIndex = 0;
            // 
            // listBox2
            // 
            listBox2.Dock = DockStyle.Fill;
            listBox2.FormattingEnabled = true;
            listBox2.Location = new Point(396, 0);
            listBox2.MultiColumn = true;
            listBox2.Name = "listBox2";
            listBox2.Size = new Size(288, 209);
            listBox2.TabIndex = 1;
            // 
            // listBox1
            // 
            listBox1.Dock = DockStyle.Left;
            listBox1.FormattingEnabled = true;
            listBox1.Location = new Point(0, 0);
            listBox1.Name = "listBox1";
            listBox1.Size = new Size(396, 209);
            listBox1.TabIndex = 0;
            // 
            // panel4
            // 
            panel4.Controls.Add(checkBox1);
            panel4.Controls.Add(bStop);
            panel4.Controls.Add(bStart);
            panel4.Dock = DockStyle.Right;
            panel4.Location = new Point(684, 0);
            panel4.Name = "panel4";
            panel4.Size = new Size(155, 209);
            panel4.TabIndex = 1;
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(26, 33);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(75, 21);
            checkBox1.TabIndex = 4;
            checkBox1.Text = "禁录声音";
            checkBox1.UseVisualStyleBackColor = true;
            // 
            // bStop
            // 
            bStop.Location = new Point(17, 154);
            bStop.Name = "bStop";
            bStop.Size = new Size(104, 43);
            bStop.TabIndex = 3;
            bStop.Text = "Stop (&T)";
            bStop.UseVisualStyleBackColor = true;
            bStop.Click += bStop_Click;
            // 
            // bStart
            // 
            bStart.Location = new Point(17, 88);
            bStart.Name = "bStart";
            bStart.Size = new Size(103, 45);
            bStart.TabIndex = 2;
            bStart.Text = "Start (&S)";
            bStart.UseVisualStyleBackColor = true;
            bStart.Click += bStart_Click;
            // 
            // fMain
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(839, 508);
            Controls.Add(panel1);
            Controls.Add(panel2);
            Name = "fMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "SRS (Multiple audio inputs can be select Ctrl & Shift)";
            Load += fMain_Load;
            panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            panel2.ResumeLayout(false);
            panel3.ResumeLayout(false);
            panel4.ResumeLayout(false);
            panel4.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel panel1;
        private PictureBox pictureBox1;
        private Panel panel2;
        private Panel panel4;
        private Panel panel3;
        private ListBox listBox1;
        private Button bStop;
        private Button bStart;
        private ListBox listBox2;
        private CheckBox checkBox1;
    }
}
