namespace MMDViewer
{
    partial class Form1
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
            components = new System.ComponentModel.Container();
            label1 = new Label();
            glControl1 = new OpenTK.GLControl.GLControl();
            button1 = new Button();
            label2 = new Label();
            checkBox1 = new CheckBox();
            toolTip1 = new ToolTip(components);
            linkLabel1 = new LinkLabel();
            button2 = new Button();
            button3 = new Button();
            button4 = new Button();
            checkBox2 = new CheckBox();
            button5 = new Button();
            button6 = new Button();
            button7 = new Button();
            button8 = new Button();
            button9 = new Button();
            label3 = new Label();
            openFileDialog1 = new OpenFileDialog();
            button10 = new Button();
            checkBox3 = new CheckBox();
            saveFileDialog1 = new SaveFileDialog();
            button11 = new Button();
            checkBox4 = new CheckBox();
            button12 = new Button();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AllowDrop = true;
            label1.AutoSize = true;
            label1.Location = new Point(0, 0);
            label1.Name = "label1";
            label1.Size = new Size(318, 21);
            label1.TabIndex = 0;
            label1.Text = "将PMX模型拖入到本程序中即可预览模型。";
            label1.DragDrop += Form1_DragDrop;
            label1.DragEnter += Form1_DragEnter;
            // 
            // glControl1
            // 
            glControl1.AllowDrop = true;
            glControl1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            glControl1.API = OpenTK.Windowing.Common.ContextAPI.OpenGL;
            glControl1.APIVersion = new Version(3, 3, 0, 0);
            glControl1.Flags = OpenTK.Windowing.Common.ContextFlags.Default;
            glControl1.IsEventDriven = true;
            glControl1.Location = new Point(0, 24);
            glControl1.Name = "glControl1";
            glControl1.Profile = OpenTK.Windowing.Common.ContextProfile.Core;
            glControl1.SharedContext = null;
            glControl1.Size = new Size(500, 500);
            glControl1.TabIndex = 1;
            glControl1.Load += glControl1_Load;
            glControl1.DragDrop += Form1_DragDrop;
            glControl1.DragEnter += Form1_DragEnter;
            glControl1.Paint += glControl1_Paint;
            glControl1.MouseDown += glControl1_MouseDown;
            glControl1.MouseMove += glControl1_MouseMove;
            glControl1.MouseUp += glControl1_MouseUp;
            glControl1.MouseWheel += glControl1_MouseWheel;
            glControl1.Resize += glControl1_Resize;
            // 
            // button1
            // 
            button1.AllowDrop = true;
            button1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button1.Location = new Point(561, 137);
            button1.Name = "button1";
            button1.Size = new Size(82, 30);
            button1.TabIndex = 10;
            button1.Text = "重置视角";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            button1.DragDrop += Form1_DragDrop;
            button1.DragEnter += Form1_DragEnter;
            // 
            // label2
            // 
            label2.AllowDrop = true;
            label2.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            label2.Location = new Point(506, 9);
            label2.Name = "label2";
            label2.Size = new Size(193, 68);
            label2.TabIndex = 12;
            label2.Text = "操作指南：\r\n左键拖拽旋转，右键拖拽平移，滚轮缩放。";
            label2.DragDrop += Form1_DragDrop;
            label2.DragEnter += Form1_DragEnter;
            // 
            // checkBox1
            // 
            checkBox1.AllowDrop = true;
            checkBox1.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(506, 429);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(173, 25);
            checkBox1.TabIndex = 15;
            checkBox1.Text = "开启光照（实验性）";
            checkBox1.UseVisualStyleBackColor = true;
            checkBox1.CheckedChanged += checkBox1_CheckedChanged;
            checkBox1.DragDrop += Form1_DragDrop;
            checkBox1.DragEnter += Form1_DragEnter;
            // 
            // linkLabel1
            // 
            linkLabel1.AllowDrop = true;
            linkLabel1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            linkLabel1.AutoSize = true;
            linkLabel1.Location = new Point(552, 77);
            linkLabel1.Name = "linkLabel1";
            linkLabel1.Size = new Size(100, 21);
            linkLabel1.TabIndex = 17;
            linkLabel1.TabStop = true;
            linkLabel1.Text = "我的B站主页";
            linkLabel1.LinkClicked += linkLabel1_LinkClicked;
            linkLabel1.DragDrop += Form1_DragDrop;
            linkLabel1.DragEnter += Form1_DragEnter;
            // 
            // button2
            // 
            button2.AllowDrop = true;
            button2.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button2.Location = new Point(519, 101);
            button2.Name = "button2";
            button2.Size = new Size(53, 30);
            button2.TabIndex = 18;
            button2.Text = "正视";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            button2.DragDrop += Form1_DragDrop;
            button2.DragEnter += Form1_DragEnter;
            // 
            // button3
            // 
            button3.AllowDrop = true;
            button3.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button3.Location = new Point(578, 101);
            button3.Name = "button3";
            button3.Size = new Size(53, 30);
            button3.TabIndex = 19;
            button3.Text = "俯视";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
            button3.DragDrop += Form1_DragDrop;
            button3.DragEnter += Form1_DragEnter;
            // 
            // button4
            // 
            button4.AllowDrop = true;
            button4.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button4.Location = new Point(637, 101);
            button4.Name = "button4";
            button4.Size = new Size(53, 30);
            button4.TabIndex = 20;
            button4.Text = "侧视";
            button4.UseVisualStyleBackColor = true;
            button4.Click += button4_Click;
            button4.DragDrop += Form1_DragDrop;
            button4.DragEnter += Form1_DragEnter;
            // 
            // checkBox2
            // 
            checkBox2.AllowDrop = true;
            checkBox2.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            checkBox2.AutoSize = true;
            checkBox2.Location = new Point(506, 460);
            checkBox2.Name = "checkBox2";
            checkBox2.Size = new Size(137, 25);
            checkBox2.TabIndex = 21;
            checkBox2.Text = "MMD操作风格";
            checkBox2.UseVisualStyleBackColor = true;
            checkBox2.CheckedChanged += checkBox2_CheckedChanged;
            checkBox2.DragDrop += Form1_DragDrop;
            checkBox2.DragEnter += Form1_DragEnter;
            // 
            // button5
            // 
            button5.AllowDrop = true;
            button5.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button5.Enabled = false;
            button5.Location = new Point(519, 266);
            button5.Name = "button5";
            button5.Size = new Size(82, 30);
            button5.TabIndex = 22;
            button5.Text = "加载动作";
            button5.UseVisualStyleBackColor = true;
            button5.Click += button5_Click;
            button5.DragDrop += Form1_DragDrop;
            button5.DragEnter += Form1_DragEnter;
            // 
            // button6
            // 
            button6.AllowDrop = true;
            button6.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button6.Enabled = false;
            button6.Location = new Point(519, 217);
            button6.Name = "button6";
            button6.Size = new Size(53, 30);
            button6.TabIndex = 25;
            button6.Text = "播放";
            button6.UseVisualStyleBackColor = true;
            button6.Click += button6_Click;
            button6.DragDrop += Form1_DragDrop;
            button6.DragEnter += Form1_DragEnter;
            // 
            // button7
            // 
            button7.AllowDrop = true;
            button7.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button7.Enabled = false;
            button7.Location = new Point(578, 217);
            button7.Name = "button7";
            button7.Size = new Size(53, 30);
            button7.TabIndex = 24;
            button7.Text = "暂停";
            button7.UseVisualStyleBackColor = true;
            button7.Click += button7_Click;
            button7.DragDrop += Form1_DragDrop;
            button7.DragEnter += Form1_DragEnter;
            // 
            // button8
            // 
            button8.AllowDrop = true;
            button8.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button8.Enabled = false;
            button8.Location = new Point(637, 217);
            button8.Name = "button8";
            button8.Size = new Size(53, 30);
            button8.TabIndex = 23;
            button8.Text = "停止";
            button8.UseVisualStyleBackColor = true;
            button8.Click += button8_Click;
            button8.DragDrop += Form1_DragDrop;
            button8.DragEnter += Form1_DragEnter;
            // 
            // button9
            // 
            button9.AllowDrop = true;
            button9.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button9.Enabled = false;
            button9.Location = new Point(607, 266);
            button9.Name = "button9";
            button9.Size = new Size(86, 30);
            button9.TabIndex = 27;
            button9.Text = "加载镜头";
            button9.UseVisualStyleBackColor = true;
            button9.Click += button9_Click;
            button9.DragDrop += Form1_DragDrop;
            button9.DragEnter += Form1_DragEnter;
            // 
            // label3
            // 
            label3.AllowDrop = true;
            label3.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label3.AutoSize = true;
            label3.Location = new Point(0, 527);
            label3.Name = "label3";
            label3.Size = new Size(0, 21);
            label3.TabIndex = 28;
            label3.DragDrop += Form1_DragDrop;
            label3.DragEnter += Form1_DragEnter;
            // 
            // openFileDialog1
            // 
            openFileDialog1.FileName = "openFileDialog1";
            // 
            // button10
            // 
            button10.AllowDrop = true;
            button10.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            button10.Location = new Point(607, 393);
            button10.Name = "button10";
            button10.Size = new Size(65, 30);
            button10.TabIndex = 29;
            button10.Text = "About";
            button10.UseVisualStyleBackColor = true;
            button10.Click += button10_Click;
            button10.DragDrop += Form1_DragDrop;
            button10.DragEnter += Form1_DragEnter;
            // 
            // checkBox3
            // 
            checkBox3.AllowDrop = true;
            checkBox3.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            checkBox3.AutoSize = true;
            checkBox3.Enabled = false;
            checkBox3.Location = new Point(506, 491);
            checkBox3.Name = "checkBox3";
            checkBox3.Size = new Size(125, 25);
            checkBox3.TabIndex = 30;
            checkBox3.Text = "报告模型问题";
            checkBox3.UseVisualStyleBackColor = true;
            checkBox3.CheckedChanged += checkBox3_CheckedChanged;
            checkBox3.DragDrop += Form1_DragDrop;
            checkBox3.DragEnter += Form1_DragEnter;
            // 
            // button11
            // 
            button11.AllowDrop = true;
            button11.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            button11.Location = new Point(519, 393);
            button11.Name = "button11";
            button11.Size = new Size(65, 30);
            button11.TabIndex = 31;
            button11.Text = "截图";
            button11.UseVisualStyleBackColor = true;
            button11.Click += button11_Click;
            button11.DragDrop += Form1_DragDrop;
            button11.DragEnter += Form1_DragEnter;
            // 
            // checkBox4
            // 
            checkBox4.AllowDrop = true;
            checkBox4.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            checkBox4.AutoSize = true;
            checkBox4.Location = new Point(506, 522);
            checkBox4.Name = "checkBox4";
            checkBox4.Size = new Size(205, 25);
            checkBox4.TabIndex = 32;
            checkBox4.Text = "启用物理演算（实验性）";
            checkBox4.UseVisualStyleBackColor = true;
            checkBox4.DragDrop += Form1_DragDrop;
            checkBox4.DragEnter += Form1_DragEnter;
            checkBox4.CheckedChanged += checkBox4_CheckedChanged;
            // 
            // button12
            // 
            button12.AllowDrop = true;
            button12.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button12.Enabled = false;
            button12.Location = new Point(561, 302);
            button12.Name = "button12";
            button12.Size = new Size(94, 30);
            button12.TabIndex = 33;
            button12.Text = "清除VMD";
            button12.UseVisualStyleBackColor = true;
            button12.Click += button12_Click;
            button12.DragDrop += Form1_DragDrop;
            button12.DragEnter += Form1_DragEnter;
            // 
            // Form1
            // 
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 550);
            Controls.Add(button12);
            Controls.Add(checkBox4);
            Controls.Add(button11);
            Controls.Add(checkBox3);
            Controls.Add(button10);
            Controls.Add(label3);
            Controls.Add(button9);
            Controls.Add(button6);
            Controls.Add(button7);
            Controls.Add(button8);
            Controls.Add(button5);
            Controls.Add(checkBox2);
            Controls.Add(button4);
            Controls.Add(button3);
            Controls.Add(button2);
            Controls.Add(linkLabel1);
            Controls.Add(checkBox1);
            Controls.Add(label2);
            Controls.Add(button1);
            Controls.Add(label1);
            Controls.Add(glControl1);
            Font = new Font("Microsoft YaHei UI", 12F);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "MMDViewer by：蔴球视频";
            FormClosed += Form1_FormClosed;
            DragDrop += Form1_DragDrop;
            DragEnter += Form1_DragEnter;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private OpenTK.GLControl.GLControl glControl1;
        private Button button1;
        private Label label2;
        private ToolTip toolTip1;
        private CheckBox checkBox1;
        private LinkLabel linkLabel1;
        private Button button2;
        private Button button3;
        private Button button4;
        private CheckBox checkBox2;
        private Button button5;
        private Button button6;
        private Button button7;
        private Button button8;
        private Button button9;
        private Label label3;
        private OpenFileDialog openFileDialog1;
        private Button button10;
        private CheckBox checkBox3;
        private SaveFileDialog saveFileDialog1;
        private Button button11;
        private CheckBox checkBox4;
        private Button button12;
    }
}
