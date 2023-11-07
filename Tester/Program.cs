using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions1;
using System.Windows.Forms;

namespace Tester
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
        //    string text = "Hello! 😊👩🏽‍🚒🌟🎉";

        //    // Match emojis using custom regex pattern
        //    string pattern =
        //        @"[\u2600-\u27BF\u2B00-\u2BFF\u1F300-\u1F5FF\u1F600-\u1F64F\u1F680-\u1F6FF\u1F900-\u1F9FF\uD83C\uD83D\uD83E]" +
        //         "[\uDC00-\uDFFF]" +
        //         "|\uD83C" +
        //         "[\uDC00-\uDFFF]" +
        //         "|\uD83E" +
        //         "[\uDD00-\uDDFF]";
        //    MatchCollection matches = Regex.Matches(text, pattern);

        //    // Print matched emojis
        //    foreach (Match match in matches)
        //    {
        //        Console.WriteLine(match.Value);
        //    }



            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
