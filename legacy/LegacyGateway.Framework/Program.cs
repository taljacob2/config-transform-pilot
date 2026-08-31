using System;
using System.Configuration;

namespace LegacyGateway.Framework
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string apiUrl = ConfigurationManager.AppSettings["ApiUrl"];
            Console.WriteLine("LegacyGateway starting against " + apiUrl);
        }
    }
}
