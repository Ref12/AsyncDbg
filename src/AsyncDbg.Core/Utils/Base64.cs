using AsyncDbg;

namespace Codex.Utilities
{
    internal class Base64
    {
        public enum Format
        {
            LowercaseAlphanumeric,
            UrlSafe,
        }

        public static readonly char[][] base64Formats = new char[][]
        {
            // LowercaseAlphanumeric
            new char[] {'a','b','c','d','e','f','g','h','i','j','k','l','m','n','o',
                        'p','q','r','s','t','u','v','w','x','y','z','a','b','c','d',
                        'e','f','g','h','i','j','k','l','m','n','o','p','q','r','s',
                        't','u','v','w','x','y','z','0','1','2','3','4','5','6','7',
                        '8','9','z','y' },

            // UrlSafe
            new char[] {'A','B','C','D','E','F','G','H','I','J','K','L','M','N','O',
                        'P','Q','R','S','T','U','V','W','X','Y','Z','a','b','c','d',
                        'e','f','g','h','i','j','k','l','m','n','o','p','q','r','s',
                        't','u','v','w','x','y','z','0','1','2','3','4','5','6','7',
                        '8','9','-','_' },
        };

        internal static unsafe string ToBase64String(byte* inData, int offset, int length, int? maxCharLength = null, Format format = Format.UrlSafe)
        {
            Contract.Assert(length < 1024, "This method is not intended for conversion of long (> 1024 bytes) byte arrays.");

            int lengthmod3 = length % 3;
            int calcLength = offset + (length - lengthmod3);
            int j = 0;
            int charLength = ((length * 8) + 5) / 6;

            //Convert three bytes at a time to base64 notation.  This will consume 4 chars.
            int i;

            // get a pointer to the base64Table to avoid unnecessary range checking
            char* outChars = stackalloc char[charLength];
            var table = base64Formats[(int)format];
            fixed (char* base64 = table)
            {
                for (i = offset; i < calcLength; i += 3)
                {
                    outChars[j] = base64[(inData[i] & 0xfc) >> 2];
                    outChars[j + 1] = base64[((inData[i] & 0x03) << 4) | ((inData[i + 1] & 0xf0) >> 4)];
                    outChars[j + 2] = base64[((inData[i + 1] & 0x0f) << 2) | ((inData[i + 2] & 0xc0) >> 6)];
                    outChars[j + 3] = base64[(inData[i + 2] & 0x3f)];
                    j += 4;
                }

                //Where we left off before
                i = calcLength;

                switch (lengthmod3)
                {
                    case 2: //One character padding needed
                        outChars[j] = base64[(inData[i] & 0xfc) >> 2];
                        outChars[j + 1] = base64[((inData[i] & 0x03) << 4) | ((inData[i + 1] & 0xf0) >> 4)];
                        outChars[j + 2] = base64[(inData[i + 1] & 0x0f) << 2];
                        j += 4;
                        break;
                    case 1: // Two character padding needed
                        outChars[j] = base64[(inData[i] & 0xfc) >> 2];
                        outChars[j + 1] = base64[(inData[i] & 0x03) << 4];
                        j += 4;
                        break;
                }
            }

            return new string(outChars, 0, maxCharLength ?? charLength);
        }
    }
}
