using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;

namespace PHD2_cmds_proxy
{
   class Program
   {
      /// <summary>
      /// Generate extra logging output to the console.
      /// </summary>
      public static bool verbose = false;
      /// <summary>
      /// Display the usage.
      /// </summary>
      private static bool showHelp = false;
      /// <summary>
      /// Single json string command to send verbatim to PHD/2.
      /// </summary>
      private static string command;
      /// <summary>
      /// Single regex json response to expect in response to the command sent. If command is specified, this need not be.
      /// </summary>
      private static string response;

      /// <summary>
      /// TCP/IP hostname.
      /// </summary>
      private static string host = "127.0.0.1";

      /// <summary>
      /// TCP/IP port to connect to on host.
      /// </summary>
      private static int port = 4400;

      private static OptionSet cmdOptions = new OptionSet
      {
		   { "v|verbose", "Get extra spammy output including step by step logging of communications.", v => { verbose = (v != null); } },
		   { "c|command=", "Quoted json command to send verbatim to PHD/2. --response is optional with --command.",   v => { command = v; } },
		   { "r|response=", "Regular expression that matches json response from PHD/2 that indicates command is done. Requires --command be specified.",   v => { response = v; } },
		   { "h|host=", "Optional host to connect to. Default is localhost if none specified.",   v => { host = v; } },
		   { "p|port=", "Optional port on host to connect to. Default is port 4400 if none specified.",   v => { port = int.Parse(v); } },
		   { "?|help", "Display this help.",  v => { showHelp = v != null; } },
   	};

      private static void ShowHelp()
      {
         Console.WriteLine("Usage: PHD2_cmds_proxy {--verbose|help} [--command=\"json string\" {--response=\"regex to match json string response\"}] {--host=hostname {--port=number}}");
         Console.WriteLine("Send commands over TCP/IP to PHD/2 and optionally handle responses.");
         Console.WriteLine();
         Console.WriteLine("Options:");
         cmdOptions.WriteOptionDescriptions(Console.Out);
      }

      /// <summary>
      /// Validate combinations of command line options.
      /// </summary>
      /// <param name="decodedOptions"></param>
      /// <returns></returns>
      private static bool argsAreValid()
      {
         if (String.IsNullOrEmpty(command))
         {
            return false;
         }
         else
            return true;
      }

      static void Main(string[] args)
      {
         List<string> unknowns = new List<string>();
         try
         {
            unknowns = cmdOptions.Parse(args);
         }
         catch(OptionException e)
         {
            System.Console.WriteLine("Invalid parameter(s).");
            System.Console.WriteLine(e.Message);
            System.Console.WriteLine("Try --help for options.");
            return;
         }

         if (showHelp || unknowns.Count > 0 || !argsAreValid())
         {
            if (unknowns.Count > 0)
            {
               System.Console.WriteLine("Unknown options found:");
               foreach(string opt in unknowns)
               {
                  System.Console.WriteLine(opt);
               }
            }
            ShowHelp();
         }
         else
         {
            if (verbose)
            {
               System.Console.WriteLine("Args are all valid.");
            }

            PHD2Client client = new PHD2Client(host, port);
            if (verbose)
            {
               System.Console.WriteLine("Connecting to " + host + ":" + port.ToString());
            }

            while (!client.isConnected && !client.isDone)
            {
               if (client.connectedEvent.WaitOne(1000))
                  break;
            }

            if (!client.isDone)
            {
               while (!client.isHandshakeComplete && !client.isDone)
               {
                  if (client.handshakeCompleteEvent.WaitOne(1000))
                     break;
               }

               if (!client.isDone)
               {
                  if (verbose && client.isHandshakeComplete)
                  {
                     System.Console.WriteLine("Handshake complete...");
                  }

                  client.send(command, response);

                  while (!client.isDone)
                  {
                     if (client.doneEvent.WaitOne(1000))
                        break;
                     if (verbose)
                     {
                        System.Console.Write("<=>");
                        System.Console.WriteLine("Progress Tx=" + client.progressTx.ToString() + " Rx=" + client.progressRx.ToString());
                     }
                  }
               }
            }
         }
      }
   }
}
