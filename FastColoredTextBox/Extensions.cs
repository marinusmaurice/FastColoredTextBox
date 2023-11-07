using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastColoredTextBoxNS
{
    public static class Extensions
    {
        public static bool IsLetterOrDigit(this string value)
        {
            foreach (char item in value)
            {
                if (!char.IsLetterOrDigit(item))
                    return false;
            }

            return true;
        }

        public static bool IsDigit(this string value)
        {
            foreach (char item in value)
            {
                if (!char.IsDigit(item))
                    return false;
            }

            return true;
        }
    }
}
