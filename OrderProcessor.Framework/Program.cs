using System;
using System.Configuration;

var apiUrl = ConfigurationManager.AppSettings["ApiUrl"];
Console.WriteLine($"OrderProcessor starting against {apiUrl}");
