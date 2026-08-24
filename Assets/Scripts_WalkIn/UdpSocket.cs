using UnityEngine;
using System.Collections;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Unity.Services.Relay.Models;

public class UdpSocket : MonoBehaviour
{
    [HideInInspector] public bool isTxStarted = false;

    [SerializeField] string IP = "127.0.0.1"; // local host
    [SerializeField] int rxPort = 8000; // port to receive data from Python on
    [SerializeField] int txPort = 8001; // port to send data to Python on

    int i = 0; // DELETE THIS: Added to show sending data from Unity to Python via UDP

    // Create necessary UdpClient objects
    UdpClient client;
    IPEndPoint remoteEndPoint;
    Thread receiveThread; // Receiving Thread

    Sender sender;
    public Regions regions;

    volatile bool keepReceiving = false;


    public void SendData(string message) // Use to send data to Python
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, remoteEndPoint);
        }
        catch (Exception err)
        {
            print(err.ToString());
        }
    }

    void Awake()
    {
        // Create remote endpoint (to Matlab) 
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(IP), txPort);

        // Create local client
        client = new UdpClient(rxPort);

        // local endpoint define (where messages are received)
        // Create a new thread for reception of incoming messages
        keepReceiving = true;
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        // Initialize (seen in comments window)
        print("UDP Comms Initialised");

    }

    private void Start()
    {
        sender = FindObjectOfType<Sender>(); // Instead of using a public variable
    }



    // Receive data, update packets received
    // ��� update �ϰ� ������� ������ ����Ǵµ�?
    private void ReceiveData()
    {

        // ���⼭ ������ �ɾ���ϳ�?
        //if(regions.GetComponent<Regions>().receiveFromPython == true)
        while (keepReceiving)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);

                // here you print out what has been sent
                print(">> " + text);

                // ��� voronoi �ɰ��� �� ���� �ϸ� �ɵ�
                ProcessInput(text);
            }
            catch (Exception err)
            {
                // client.Close() in OnDisable breaks the blocking Receive() above;
                // keepReceiving is already false by then, so just exit instead of looping on a closed socket.
                if (!keepReceiving)
                    break;
                print(err.ToString());
            }
        }
    }

    private void ProcessInput(string input)
    {
        // PROCESS INPUT RECEIVED STRING HERE
        //pythonTest.UpdatePythonRcvdText(input); 

        // �� �̷��� �ɰ��� voronoi polygon���� �����°���!! �״�� �׷��ֱ⸸ �ϸ� ��!!
        // �긦 ó���ϴ� �Լ��� sender



        if (!isTxStarted) // First data arrived so tx started
        {
            isTxStarted = true;
        }
    }

    //Prevent crashes - close clients and threads properly!
    void OnDisable()
    {
        // Signal the loop to stop, then close the socket to unblock the pending Receive() call.
        // Thread.Abort() doesn't reliably interrupt a thread blocked in a native socket call,
        // and was previously letting the thread spin forever on a closed client.
        keepReceiving = false;
        client?.Close();
    }
}