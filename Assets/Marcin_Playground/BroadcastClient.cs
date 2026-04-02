using UnityEngine;
using System.Net;
using System.Net.Sockets;
using TMPro;

public class BroadcastClient : MonoBehaviour
{
    public TMP_Text doorNumber;

    public string myIp;
    private string serverIP;


    //private string myIP;

    void Start()
    {
        myIp = GetLocalIPAddress();
        SetDoorNumber();
    }

    void SetDoorNumber()
    {
        var parts = GetLocalIPAddress().Split('.');

        if (doorNumber)
            doorNumber.text = parts[3];
    }

    public string GetLocalIPAddress()
    {
        /*
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                myIp = ip.ToString();
                return myIp;
            }
        }
        return "0";
        }*/
        myIp = WebSocketClientV6.Instance.selfIP;
        //myIp = WebSocketClientV7.Instance.selfIP;
        return myIp;
    }
}