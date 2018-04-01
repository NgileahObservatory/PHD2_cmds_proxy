using System;  
using System.Net;  
using System.Net.Sockets;  
using System.Threading;  
using System.Text;
using System.Text.RegularExpressions;

namespace PHD2_cmds_proxy
{
   // State object for receiving data from remote device.  
   public class StateObject 
   {  
       // Client socket.  
       public Socket workSocket = null;  
       // Size of receive buffer.  
       public const int BufferSize = 256;  
       // Receive buffer.  
       public byte[] buffer = new byte[BufferSize];  
       // Received data string.  
       public StringBuilder sb = new StringBuilder();
  
      public Action<StateObject> nextAction = null;

      public StateObject(Action<StateObject> nextAction)
      {
         this.nextAction = nextAction;
      }

      public StateObject()
      {
      }
   }

   class PHD2Client
   {
      private Socket socket = null; 

      /// <summary>
      /// Constructor where json scripted commands and responses are in a file.
      /// </summary>
      /// <param name="host"></param>
      /// <param name="port"></param>
      public PHD2Client(string host, int port)
      {
         StartClient(host, port);
      }

      private void StartClient(string host, int port)
      {
         // Connect to a remote device.  
         // Establish the remote endpoint for the socket.  
         // The name of the   
         // remote device is "localhost".  
         IPAddress[] ipAddresses = Dns.GetHostAddresses(host);
         if (ipAddresses.Length == 0)
         {
            throw new IndexOutOfRangeException("Zero IP addresses returned for given host " + host);
         }

         IPAddress firstIpAddress = ipAddresses[0];
         IPEndPoint remoteEP = new IPEndPoint(firstIpAddress, port);

         // Create a TCP/IP socket.  
         socket = new Socket(firstIpAddress.AddressFamily,
               SocketType.Stream, ProtocolType.Tcp);

         // Connect to the remote endpoint.  
         socket.BeginConnect(remoteEP,
               new AsyncCallback(ConnectCallback), new StateObject(
         (StateObject parent) =>
         {
            this.Receive(new StateObject(
               (StateObject state) =>
               {
                  isHandshakeComplete = state.sb.ToString().Contains("\"Event\":\"AppState\"");
                  if (isHandshakeComplete)
                  {
                     // Handshake is complete. There is no next action.
                     state.nextAction = null;
                  }
               }));
         }));
      }

      public ManualResetEvent connectedEvent = new ManualResetEvent(false);
      private bool connected = false;
      public bool isConnected
      {
         get
         {
            return connected;
         }
         set
         {
            connected = value;
            if (connected)
            {
               connectedEvent.Set();
            }
         }
      }

      private void ConnectCallback(IAsyncResult ar)
      {
         try
         {
            // Retrieve the socket from the state object.  
            StateObject onConnect = (StateObject) ar.AsyncState;

            // Complete the connection.  
            socket.EndConnect(ar);

            if (Program.verbose)
            {
               Console.WriteLine("Socket connected to {0}",
                   socket.RemoteEndPoint.ToString());
            }

            isConnected = true;
            // Do whatever was asked on connect.
            onConnect.nextAction(onConnect);
         }
         catch (Exception e)
         {
            isDone = true;
            Console.WriteLine(e.ToString());
         }
      }

      private int tx = 0;
      public int progressTx
      {
         get
         {
            return tx;
         }
         set
         {
            tx = value;
         }
      }

      private int rx = 0;
      public int progressRx
      {
         get
         {
            return rx;
         }
         set
         {
            rx = value;
         }
      }

      public ManualResetEvent doneEvent = new ManualResetEvent(false);
      private bool done = false;
      public bool isDone
      {
         get
         {
            return done;
         }
         set
         {
            done = value;
            if (done)
            {
               doneEvent.Set();
            }
         }
      }

      private void Receive(StateObject state)
      {
         try
         {
            // Begin receiving the data from the remote device.  
            socket.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReceiveCallback), state);
         }
         catch (Exception e)
         {
            Console.WriteLine(e.ToString());
         }
      }

      public ManualResetEvent handshakeCompleteEvent = new ManualResetEvent(false);
      private bool handshakeComplete = false;
      public bool isHandshakeComplete
      {
         get 
         { 
            return handshakeComplete; 
         }
         set 
         { 
            handshakeComplete = value; 
            if (handshakeComplete)
            {
               handshakeCompleteEvent.Set();
            }
         }
      }

      private void ReceiveCallback(IAsyncResult ar)
      {
         try
         {
            // Retrieve the state object and the client socket   
            // from the asynchronous state object.  
            StateObject state = (StateObject)ar.AsyncState;

            // Read data from the remote device.  
            int bytesRead = socket.EndReceive(ar);

            if (bytesRead > 0)
            {
               // There might be more data, so store the data received so far.  
               state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));

               rx += bytesRead;
               if (Program.verbose)
               {
                  System.Console.WriteLine(state.sb.ToString());
               }
               state.nextAction(state);

               if (state.nextAction != null)
               {
                  // Get the rest of the data.  
                  socket.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                      new AsyncCallback(ReceiveCallback), state);
               }
            }
         }
         catch (Exception e)
         {
            Console.WriteLine(e.ToString());
         }
      }

      public void send(string command, string response)
      {
         Send(socket, command, new StateObject((StateObject parent) => 
         {
            if(String.IsNullOrEmpty(response))
            {
               // No response means we want to send and exit.
               parent.nextAction = null;
               isDone = true;
            }
            else
            {
               // Otherwise we want to keep receiving until 
               // we get the PHD/2 response we are looking for.
               Receive(new StateObject((StateObject state) =>
               {
                  Regex responseRegex = new Regex(response, RegexOptions.IgnoreCase);
                  if (responseRegex.Matches(state.sb.ToString()).Count > 0)
                  {
                     parent.nextAction = null;
                     isDone = true;
                  }
               }));
            }
         }));
      }

      private void Send(Socket client, String data, StateObject state)
      {
         // Convert the string data to byte data using ASCII encoding.
         if (!data.EndsWith("\n"))
            data += "\n";
         byte[] byteData = Encoding.ASCII.GetBytes(data);
         if (Program.verbose)
            System.Console.WriteLine("Sending " + data + " to server.");
         // Begin sending the data to the remote device.  
         client.BeginSend(byteData, 0, byteData.Length, 0,
             new AsyncCallback(SendCallback), state);
      }

      private void SendCallback(IAsyncResult ar)
      {
         try
         {
            StateObject state = (StateObject)ar.AsyncState;

            // Complete sending the data to the remote device.  
            int bytesSent = socket.EndSend(ar);
            if (Program.verbose)
               Console.WriteLine("Sent {0} bytes to server.", bytesSent);
            tx += bytesSent;

            state.nextAction(state);
         }
         catch (Exception e)
         {
            Console.WriteLine(e.ToString());
         }
      }
   }
}
