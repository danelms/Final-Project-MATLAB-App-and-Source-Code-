using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EyeGazeInputManager : MonoBehaviour
{
    private float _xPos;
    private float _yPos;
    private bool _run;
    [SerializeField]
    private bool _calibrated = false;
    [SerializeField]
    private ClientSocket _socket = null;
    private static EyeGazeInputManager _instance;
    public static EyeGazeInputManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<EyeGazeInputManager>();
                if (_instance == null)
                {
                    GameObject singletonObject = new GameObject(typeof(EyeGazeInputManager).Name);
                    _instance = singletonObject.AddComponent<EyeGazeInputManager>();
                }
            }
            return _instance;
        }
    }
    private SpriteRenderer _ccTop = null;
    private SpriteRenderer _ccBot = null;
    private SpriteRenderer _ccRight = null;
    private SpriteRenderer _ccLeft = null;
    private SpriteRenderer[] _calCircles = new SpriteRenderer[4];
    private GameObject _calCircContainer = null;
    private bool _inCalibrationScene = false;
    private Player1 _player = null;

    void Awake()
    {
        //if an instance of this object already exists
        if (_instance != null && _instance != this)
        {
            Debug.Log("Duplicate EyeGazeInputManager destroyed");
            Destroy(gameObject);
        }
        else
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    void OnApplicationQuit()
    {
        if (_run)
        {
            _run = false;
            SendViaSocket("STOP");
        }
    }
    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    void OnDisable() 
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    void LateUpdate()
    {
        if (_run)
        {
            string data = ReceiveViaSocket();

            if (data != null)
            {
                try
                {
                    string[] splitdata = data.Split('$');
                    _xPos = float.Parse(splitdata[0]);
                    _yPos = float.Parse(splitdata[1]);
                }
                catch (Exception e)
                {
                    Debug.LogError("Failed to extract x and y positions from data: " + e);
                }
            }
            //update position of target (position player moves toward)
            _player.setTarget(_xPos, _yPos);
            //send request for next position update
            SendViaSocket("PROCEED");
        }
        if (_inCalibrationScene)
        {
            StartCoroutine(Calibrate());
            _inCalibrationScene = false;
        }
    }

    /// <summary>
    /// Executes whenever a new scene is loaded
    /// </summary>
    public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log("OnSceneLoaded() called in EyeGazeInputManager");

        _calCircContainer = GameObject.Find("calibrationCircles");      

        if (_calCircContainer != null)
        {
            Debug.Log("Found circles");
            try
            {
                _calCircles[0] = _calCircContainer.transform.GetChild(0).GetComponent<SpriteRenderer>();
                _calCircles[1] = _calCircContainer.transform.GetChild(1).GetComponent<SpriteRenderer>();
                _calCircles[2] = _calCircContainer.transform.GetChild(2).GetComponent<SpriteRenderer>();
                _calCircles[3] = _calCircContainer.transform.GetChild(3).GetComponent<SpriteRenderer>();
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to get SpriteRenderer from child of _calCircContainer" + e);
            }
            _inCalibrationScene = true;
        }
        else
        {
            Debug.Log("Calibration circle container not found");
        }

        if (_inCalibrationScene)
        {
            Debug.Log("Calibration initialised");
            if (_socket == null)
            {
                _socket = new ClientSocket();
                _socket.SetupSocket();
            }
        }
        else
        {
            GameObject player = GameObject.Find("Player");

            if ( player != null )
            {
                Debug.Log("Player found in scene");
                _player = player.GetComponent<Player1>();
                _run = true;
                //notify server of level launch
                SendViaSocket("LAUNCH");
            }
            else
            {
                Debug.Log("Player not found");
            }
        }
    }

    IEnumerator Calibrate()
    {
        float xMin = -10f;
        float xMax = 10f;
        float yMin = -3.8f;
        float yMax = 4f;
        int passes = 3;
        string response;

        //Send the initial join request
        SendViaSocket("REQ$" + xMin + "$" + xMax + "$" + yMin + "$" + yMax + "$" + passes);
        //get response
        response = ReceiveViaSocket();

        if (response.Equals("CAL"))
        {
            bool finished = false;

            //for no. of passes
            for (int j = 0; j < passes; j++)
            {
                for (int i = 0; i < 4; i++)
                {
                    yield return new WaitForEndOfFrame();

                    //set colour of current circle to red
                    _calCircles[i].color = Color.red;

                    yield return WaitForSpaceBar();

                    //send trigger for eye data to be captured
                    SendViaSocket("TAKE");

                    //get response from server
                    response = ReceiveViaSocket();

                    if (response.Equals("NEXT"))
                    {
                        //reset colour of current circle
                        _calCircles[i].color = Color.white;
                    }
                    else if (response.Equals("WAITING"))
                    {
                        finished = true;
                        break;
                    }
                    else
                    {
                        Debug.LogError("Calibrator received unexpected data from TCP server during Calibrate()");
                        break;
                    }
                }
            }
            if (!finished)
            {
                Debug.LogError("Calibrator failed to receive WAITING flag during Calibrate()");
            }
            else
            {
                _calibrated = true;
            }
        }
        yield return null;
        //return to main menu
        SceneManager.LoadScene(0);
    }

    IEnumerator WaitForSpaceBar()
    {
        while (!Input.GetKeyDown(KeyCode.Space))
        {
            yield return null; // Wait until start of next frame
        }
        // Clear inputs / keycodes
        Input.ResetInputAxes();
        yield return new WaitForSeconds(0.1f);
    }

    public void SendViaSocket(string data)
    {
        _socket.SendData(data);
        Debug.Log("Sent \"" + data + "\" to server");
    }

    public string ReceiveViaSocket()
    {
        string data = _socket.ReceiveData();
        Debug.Log("Received \"" + data + "\" from server.");
        return data;
    }
    public bool IsCalibrated()
    {
        return _calibrated;
    }

    /// <summary>
    /// Used for TCP connectivity
    /// </summary>
    private class ClientSocket
    {
        TcpClient _socket;
        NetworkStream _stream;
        StreamWriter _streamWrite;
        StreamReader _streamRead;
        int _port = 5000;

        public void SetupSocket()
        {
            try
            {
                _socket = new TcpClient("127.0.0.1", _port);
                _socket.SendTimeout = 120000;
                _socket.ReceiveTimeout = 120000;
                _stream = _socket.GetStream();
                _streamWrite = new StreamWriter(_stream);
                _streamRead = new StreamReader(_stream);
                Debug.Log("Socket is ready!");
            }
            catch (Exception e)
            {
                Debug.Log("Socket error: " + e);
            }
        }
        public void SendData(string dataString)
        {
            try
            {
                _streamWrite.WriteLine(dataString + "\n");
                _streamWrite.Flush();
            }
            catch (Exception e)
            {
                Debug.LogError("Error sending data: " + e);
            }
        }
        public string ReceiveData()
        {
            string data = null;

            try
            {
                data = _streamRead.ReadLine();
            }
            catch (Exception e)
            {
                Debug.LogError("Error receiving data: " + e);
            }

            return data;
        }
        private void CloseConnection()
        {
            if (_streamRead != null) { _streamRead.Close(); }
            if (_streamWrite != null) { _streamWrite.Close(); }
            if (_stream != null) { _stream.Close(); }
            if (_socket != null) { _socket.Close(); }
        }
        void OnDestroy()
        {
            CloseConnection();
        }
    }
}