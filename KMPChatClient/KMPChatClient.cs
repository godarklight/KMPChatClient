using System;
using System.Threading;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using ICSharpCode.SharpZipLib.GZip;
using System.Runtime.Serialization.Formatters.Binary;
 
namespace KMPChatClient
{
    class ChatClient
    {
        static Socket chatTCPSocket;
        static String username;
        static Guid token;
        static Object SendLock = new Object();
        static String program_version = "0.1.4.0";
        static UnicodeEncoding encoder = new UnicodeEncoding();
        static Thread receieveThread;
        static Thread sendThread;
        static byte[] receive_buffer = new byte[8192];
		static bool receivedServerSettings = false;
        //static string data;
        //static byte receive_buffer = new byte();
        //static byte send_buffer = new byte();

        static void Main(string[] args)
        {
            token = new Guid(File.ReadAllLines("KMPPlayerToken.txt")[0]);
            Console.WriteLine(username);
            if (args.Length != 3)
            {
                Console.WriteLine("Type your username: ");
                username = Console.ReadLine();
                Console.WriteLine("Type the IP of the KMP Server: ");
                String address = Console.ReadLine();
                Console.WriteLine("Type the Port of the KMP Server: ");
                String port = Console.ReadLine();
                connectToServer(address, port);
            } else {
                username = args[0];
                connectToServer(args[1], args[2]);
            }
            
        }
        
        static void connectToServer(string hostname, string port) {
            IPAddress address = null;
            try {
                address = IPAddress.Parse(hostname);
            }
            catch (Exception) { }
            if (address == null) {
                try {
                    address = Dns.GetHostAddresses(hostname)[0];
                }
                catch (Exception) { }
            }
            if (address != null) {
                Console.WriteLine("Attempting to connect to " + address + " port " + port);
                IPEndPoint endpoint = new IPEndPoint(address, Convert.ToInt32(port));
                TcpClient chatTCPClient = new TcpClient();
                chatTCPClient.NoDelay = true;
                try {
                    chatTCPClient.Connect(endpoint);
                    chatTCPSocket = chatTCPClient.Client;
                if (chatTCPSocket.Connected) {
                    Console.WriteLine("Connected");
                    receieveThread = new Thread(new ThreadStart(handleConnection));
                    receieveThread.Start();
                    sendThread = new Thread(new ThreadStart(keepConnectionAlive));
                    sendThread.Start();

                    chatLoop();
                }
            }
            catch (Exception e) {
                Console.WriteLine("Failed to connect with: " + e.ToString());
            }
            } else {
                Console.WriteLine("Invalid Server Address");
            }
        }

       static void chatLoop() {
            while (true) {
                string newChatText = Console.ReadLine();
                byte[] newChatText_bytes = encoder.GetBytes(newChatText);
                sendMessage(ClientMessageID.TEXT_MESSAGE, newChatText_bytes);
            }
            
        }

		static void printIfDebug(string data) {
#if DEBUG
			Console.WriteLine(data);
#endif
		}
        
        static void keepConnectionAlive() {
            Console.WriteLine("KeepAlive thread started");
            while (true) {
				String[] status_array = new String[2];
				status_array[0] = username;
				status_array[1] = "Chatting from Chat Client";
				MemoryStream ms = new MemoryStream();
				BinaryFormatter bf = new BinaryFormatter();
				bf.Serialize(ms, status_array);
				byte[] keepalive_data = ms.ToArray();
				sendMessage(ClientMessageID.PRIMARY_PLUGIN_UPDATE, keepalive_data);
                Thread.Sleep(1000);
            }
        }

        
        static void handleConnection() {
            Console.WriteLine("Receive thread started");
            int received_buffer_length = 0;
            int received_message_index = 0;
            int message_id = 0;
            int message_size = 0;
            bool header_received = false;
            while (true) {
                //Get the header
                if (header_received == false) {
                    received_buffer_length = chatTCPSocket.Receive(receive_buffer, 8 - received_message_index, SocketFlags.None);
                    received_message_index = received_message_index + received_buffer_length;
                    if ( received_message_index == 8 ) {
                    message_id = BitConverter.ToInt32(receive_buffer, 0);
                    message_size = BitConverter.ToInt32(receive_buffer, 4);
                    if (message_size != 0) {
                        header_received = true;
                    }
                    received_buffer_length = 0;
                    received_message_index = 0;
                    }
                }
                if (header_received == true) {
                    byte[] received_message = new byte[message_size];
                    while (received_message_index < message_size) {
                        int bytes_to_receive = Math.Min(8192, message_size - received_message_index);
                        received_buffer_length = chatTCPSocket.Receive(receive_buffer, bytes_to_receive, SocketFlags.None);
                        Array.Copy(receive_buffer, 0, received_message, received_message_index, received_buffer_length);
                        received_message_index = received_message_index + received_buffer_length;
                        if (received_message_index == message_size) {
                            receieveMessage((ServerMessageID)message_id, received_message);
                            header_received = false;
                            received_buffer_length = 0;
                            received_message_index = 0;
                            message_id = 0;
                            message_size = 0;
                        }
                    }
                    
                }
            Thread.Sleep(1000);
            }
        }

        static void receieveMessage(ServerMessageID messageID, byte[] message_data) {
            switch (messageID)
            {
                case ServerMessageID.HANDSHAKE:
                    Console.WriteLine("Got Handshake - Replying");
                    sendHandshake();
                    break;
                    
                case ServerMessageID.HANDSHAKE_REFUSAL:
                    Console.WriteLine("Handshake refused, Don't know what to do!");
                    break;
                    
                case ServerMessageID.SERVER_MESSAGE:
                    Console.WriteLine("SERVER_MESSAGE: " + Encoding.UTF8.GetString(message_data));
                    break;
                    
                case ServerMessageID.TEXT_MESSAGE:
                    Console.WriteLine("TEXT_MESSAGE: " + Encoding.UTF8.GetString(message_data));
                    break;
                    
                case ServerMessageID.MOTD_MESSAGE:
                    Console.WriteLine("MOTD_MESSAGE: " + Encoding.UTF8.GetString(message_data));
                    break;
                    
                case ServerMessageID.PLUGIN_UPDATE:
                    printIfDebug("Unhandled Message: PLUGIN_UPDATE");
                    break;
                    
				case ServerMessageID.SERVER_SETTINGS:
					printIfDebug ("Unhandled Message: SERVER_SETTINGS");
					if (receivedServerSettings == false) {
						receivedServerSettings = true;
						sendSyncRequest ();
					}
				    break;
                    
                case ServerMessageID.SCREENSHOT_SHARE:
                    printIfDebug("Unhandled Message: SCREENSHOT_SHARE");
                    break;
                    
                case ServerMessageID.KEEPALIVE:
					printIfDebug("Unhandled Message: KEEPALIVE");
                    break;
                    
                case ServerMessageID.CONNECTION_END:
				    Console.WriteLine("The server sent a connection end message. You probably want to try to reconnect now.");
                    break;
                    
                case ServerMessageID.UDP_ACKNOWLEDGE:
                    printIfDebug("Unhandled Message: UDP_ACKNOWLEDGE");
				break;                    
                    
                case ServerMessageID.NULL:
                    printIfDebug("Unhandled Message: NULL");
                    break;
                    
                case ServerMessageID.CRAFT_FILE:
				    printIfDebug("Unhandled Message: CRAFT_FILE");
                    break;
                    
                case ServerMessageID.PING_REPLY:
                    printIfDebug("Unhandled Message: PING_REPLY");
                    break;
                   
                case ServerMessageID.SYNC:
                    printIfDebug("Unhandled Message: SYNC");
				    break;
                    
                case ServerMessageID.SYNC_COMPLETE:
                    printIfDebug("Unhandled Message: SYNC_COMPLETE");
                    break;

                    }
        }
        
        static void handleMessage() {
        }
        
        static void sendHandshake() {
            byte[] username_bytes = encoder.GetBytes(username);
            byte[] guid_bytes = encoder.GetBytes(token.ToString());
            byte[] version_bytes = encoder.GetBytes(program_version);
            byte[] handshake_data = new byte[4 + username_bytes.Length + 4 + guid_bytes.Length + version_bytes.Length];
            BitConverter.GetBytes(username_bytes.Length).CopyTo(handshake_data, 0);
            username_bytes.CopyTo(handshake_data, 4);
            BitConverter.GetBytes(guid_bytes.Length).CopyTo(handshake_data, 4 + username_bytes.Length);
            guid_bytes.CopyTo(handshake_data, 4 + username_bytes.Length + 4);
            version_bytes.CopyTo(handshake_data, 4 + username_bytes.Length + 4 + guid_bytes.Length);
            sendMessage(ClientMessageID.HANDSHAKE, handshake_data);
        }

		static void sendSyncRequest() {
		    byte[] update_bytes = BitConverter.GetBytes(-1);
		    sendMessage(ClientMessageID.SSYNC, update_bytes);
		}
        
        static void sendMessage(ClientMessageID id,  byte[] data) {
            lock (SendLock) {
                byte[] message_bytes = buildMessage(id ,data);
                int send_bytes = 0;
                while (send_bytes < message_bytes.Length) {
                    send_bytes += chatTCPSocket.Send(message_bytes, send_bytes, message_bytes.Length - send_bytes, SocketFlags.None);
                }
            }
        }

        static byte[] buildMessage(ClientMessageID id, byte[] data) {
            byte[] new_data;
            if (data != null) {
            new_data = Compress(data);
            } else {
            new_data = data;
            }
            byte[] build_message = new byte[8 + new_data.Length];
            byte[] id_bytes = new byte[4];
            byte[] header_bytes = new byte[4];
            id_bytes = BitConverter.GetBytes((int)id);
            header_bytes = BitConverter.GetBytes(new_data.Length);
            id_bytes.CopyTo(build_message, 0);
            header_bytes.CopyTo(build_message, 4);
            new_data.CopyTo(build_message, 8);
            return build_message;
        }
        public enum ClientMessageID
	{
		HANDSHAKE /*Username Length : Username : Version*/,
		PRIMARY_PLUGIN_UPDATE /*data*/,
		SECONDARY_PLUGIN_UPDATE /*data*/,
		TEXT_MESSAGE /*Message text*/,
		SCREEN_WATCH_PLAYER /*Player name*/,
		SCREENSHOT_SHARE /*Description Length : Description : data*/,
		KEEPALIVE,
		CONNECTION_END /*Message*/ ,
		UDP_PROBE,
		NULL,
		SHARE_CRAFT_FILE /*Craft Type Byte : Craft name length : Craft Name : File bytes*/,
		ACTIVITY_UPDATE_IN_GAME,
		ACTIVITY_UPDATE_IN_FLIGHT,
		PING,
		WARPING,
		SSYNC
	}
	public enum ServerMessageID
	{
		HANDSHAKE /*Protocol Version : Version String Length : Version String : ClientID*/,
		HANDSHAKE_REFUSAL /*Refusal message*/,
		SERVER_MESSAGE /*Message text*/,
		TEXT_MESSAGE /*Message text*/,
		MOTD_MESSAGE /*Message text*/,
		PLUGIN_UPDATE /*data*/,
		SERVER_SETTINGS /*UpdateInterval (4) : Screenshot Interval (4) : Screenshot Height (4) :  Bubble Size (8) : InactiveShips (1)*/,
		SCREENSHOT_SHARE /*Description Length : Description : data*/,
		KEEPALIVE,
		CONNECTION_END /*Message*/,
		UDP_ACKNOWLEDGE,
		NULL,
		CRAFT_FILE /*Craft Type Byte : Craft name length : Craft Name : File bytes*/,
		PING_REPLY,
		SYNC /*tick*/,
		SYNC_COMPLETE
	}
public static byte[] Compress(byte[] data, bool forceUncompressed = false)
	{
		if (data == null) return null;
		byte[] compressedData = null;
        MemoryStream ms = null;
        GZipOutputStream gzip = null;
		try
		{
			ms = new MemoryStream();
			using (BinaryWriter writer = new BinaryWriter(ms))
			{
				writer.Write(false);
				writer.Write(data, 0, data.Length);
				compressedData = ms.ToArray();
				ms.Close();                
				writer.Close();
			}
		}
		catch
		{
			return null;
		}
        finally
        {
            if (gzip != null) gzip.Dispose();
            if (ms != null) ms.Dispose();
        }
        return compressedData;
    }

    public static byte[] Decompress(byte[] data)
    {
		if (data == null) return null;
		byte[] decompressedData = null;
        MemoryStream ms = null;
        GZipInputStream gzip = null;
        try
		{
			ms = new MemoryStream(data,false);
        	using (BinaryReader reader = new BinaryReader(ms))
            {
				bool compressedFlag = reader.ReadBoolean();
				if (compressedFlag == false)
				{
					//Uncompressed
					decompressedData = reader.ReadBytes(data.Length - 1);
				}
				else
				{
					//Decompress
	                Int32 size = reader.ReadInt32();
	                gzip = new GZipInputStream(ms);
	                decompressedData = new byte[size];
	                gzip.Read(decompressedData, 0, decompressedData.Length);
	                gzip.Close();
	                ms.Close();
				}
				reader.Close();
            }
        }
		catch
		{
			return null;
		}
        finally
        {
            if (gzip != null) gzip.Dispose();
            if (ms != null) ms.Dispose();
        }
        return decompressedData;
    }
    }
}
