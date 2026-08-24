using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sender : MonoBehaviour
{


    UdpSocket udpSocket;


    private void Start()
    {
        udpSocket = FindObjectOfType<UdpSocket>();

    }

    void Update()
    {

        // send when pushed spacebar
        if (Input.GetKeyDown(KeyCode.X))
        {
            // 이 메세지를 다른데서 불러와야 할 거 같은데? 
            //SendToPython("sending");

        }


    }


    public void SendToPython(string message)
    {
        // 각 user position을 string으로 뽑아서 보낸다 
        //udpSocket.SendData("is this also transferring?");
        if (message == null)
        {
            udpSocket.SendData("there is nothing to send");
        }
        else
        {
            udpSocket.SendData(message);
        }

    }



}
