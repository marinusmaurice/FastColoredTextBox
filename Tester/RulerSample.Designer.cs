namespace Tester
{
    partial class RulerSample
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RulerSample));
            this.fctb = new FastColoredTextBoxNS.FastColoredTextBox();
            this.ruler = new FastColoredTextBoxNS.RulerHorizontal();
            this.rulerVertical1 = new FastColoredTextBoxNS.RulerVertical();
            ((System.ComponentModel.ISupportInitialize)(this.fctb)).BeginInit();
            this.SuspendLayout();
            // 
            // fctb
            // 
            this.fctb.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.fctb.AutoCompleteBracketsList = new char[] {
        '(',
        ')',
        '{',
        '}',
        '[',
        ']',
        '\"',
        '\"',
        '\'',
        '\''};
            this.fctb.AutoIndentCharsPatterns = "\r\n^\\s*[\\w\\.]+(\\s\\w+)?\\s*(?<range>=)\\s*(?<range>[^;=]+);\r\n^\\s*(case|default)\\s*[^:" +
    "]*(?<range>:)\\s*(?<range>[^;]+);\r\n";
            this.fctb.AutoScrollMinSize = new System.Drawing.Size(299, 300);
            this.fctb.BackBrush = null;
            this.fctb.BracketsHighlightStrategy = FastColoredTextBoxNS.BracketsHighlightStrategy.Strategy2;
            this.fctb.CharHeight = 15;
            this.fctb.CharWidth = 7;
            this.fctb.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.fctb.DefaultMarkerSize = 8;
            this.fctb.DisabledColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
            this.fctb.Font = new System.Drawing.Font("Consolas", 9.75F);
            this.fctb.IndentBackColor = System.Drawing.SystemColors.Control;
            this.fctb.IsReplaceMode = false;
            this.fctb.Language = FastColoredTextBoxNS.Language.CSharp;
            this.fctb.LeftBracket = '(';
            this.fctb.LeftBracket2 = '{';
            this.fctb.LeftPadding = 15;
            this.fctb.Location = new System.Drawing.Point(30, 24);
            this.fctb.Name = "fctb";
            this.fctb.Paddings = new System.Windows.Forms.Padding(0);
            this.fctb.RightBracket = ')';
            this.fctb.RightBracket2 = '}';
            this.fctb.SelectionColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))), ((int)(((byte)(255)))));
            this.fctb.ServiceColors = ((FastColoredTextBoxNS.ServiceColors)(resources.GetObject("fctb.ServiceColors")));
            this.fctb.Size = new System.Drawing.Size(428, 331);
            this.fctb.TabIndex = 0;
            this.fctb.Text = resources.GetString("fctb.Text");
            this.fctb.Zoom = 100;
            // 
            // ruler
            // 
            this.ruler.Dock = System.Windows.Forms.DockStyle.Top;
            this.ruler.Location = new System.Drawing.Point(0, 0);
            this.ruler.MaximumSize = new System.Drawing.Size(1073741824, 24);
            this.ruler.MinimumSize = new System.Drawing.Size(2, 24);
            this.ruler.Name = "ruler";
            this.ruler.Size = new System.Drawing.Size(458, 24);
            this.ruler.TabIndex = 1;
            this.ruler.Target = this.fctb;
            // 
            // rulerVertical1
            // 
            this.rulerVertical1.Dock = System.Windows.Forms.DockStyle.Left;
            this.rulerVertical1.Location = new System.Drawing.Point(0, 24);
            this.rulerVertical1.MaximumSize = new System.Drawing.Size(24, 1073741823);
            this.rulerVertical1.MinimumSize = new System.Drawing.Size(24, 0);
            this.rulerVertical1.Name = "rulerVertical1";
            this.rulerVertical1.Size = new System.Drawing.Size(24, 331);
            this.rulerVertical1.TabIndex = 2;
            this.rulerVertical1.Target = this.fctb;
            // 
            // RulerSample
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(458, 355);
            this.Controls.Add(this.rulerVertical1);
            this.Controls.Add(this.fctb);
            this.Controls.Add(this.ruler);
            this.Name = "RulerSample";
            this.Text = "Ruler Sample";
            ((System.ComponentModel.ISupportInitialize)(this.fctb)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private FastColoredTextBoxNS.FastColoredTextBox fctb;
        private FastColoredTextBoxNS.RulerHorizontal ruler;
        private FastColoredTextBoxNS.RulerVertical rulerVertical1;
    }
}