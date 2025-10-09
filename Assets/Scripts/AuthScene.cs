using System;
using System.Collections;
using System.Collections.Generic;
using DynamicSDK.Unity.Core;
using DynamicSDK.Unity.Messages;
using DynamicSDK.Unity.Messages.Auth;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AuthScene : MonoBehaviour
{
    [Header("Top")]
    [SerializeField] TMP_Text JwtResult;
    [SerializeField] Button JwtCopyButton;

    [SerializeField] TMP_InputField MessageInput;
    [SerializeField] TMP_Text Signature;
    [SerializeField] Button SignButton;

    [SerializeField] Button ShrinkWebViewOption;
    [SerializeField] TMP_Text ShrinkWebViewOptionCheck;

    [Header("Bottom")]
    [SerializeField] Button LogInButton;
    [SerializeField] Button LogOutButton;

    DynamicSDKManager m_sdk;
    DynamicSDKManifest m_manifest;
    bool m_isAuthenticating;
    int m_authResult = default;

    bool m_waitingWebviewReady = false;

    bool m_shrinkWebViewWhenSigning = true;

    void Awake()
    {
        DynamicSDKManager.OnWalletConnected += OnWalletConnected;
        DynamicSDKManager.OnWalletDisconnected += OnWalletDisconnected;
        DynamicSDKManager.OnUserAuthenticated += OnUserAuthenticated;
        DynamicSDKManager.OnJwtTokenReceived += OnJwtTokenReceived;
        DynamicSDKManager.OnSDKError += OnSDKError;
        DynamicSDKManager.OnWebViewReady += OnWebViewReady;
        DynamicSDKManager.OnWebViewClosed += OnWebViewClosed;
        DynamicSDKManager.OnMessageSigned += OnMessageSigned;

        JwtCopyButton.onClick.AddListener(CopyJwt);
        SignButton.onClick.AddListener(SignMessage);

        LogInButton.onClick.AddListener(ShowDynamicAuth);
        LogOutButton.onClick.AddListener(Disconnect);

        ShrinkWebViewOption.onClick.AddListener(() =>
        {
            m_shrinkWebViewWhenSigning = !m_shrinkWebViewWhenSigning;
            ShrinkWebViewOptionCheck.text = m_shrinkWebViewWhenSigning ? "X" : default;
        });

        m_shrinkWebViewWhenSigning = true;
        ShrinkWebViewOptionCheck.text = m_shrinkWebViewWhenSigning ? "X" : default;
    }

    void OnDestroy()
    {
        DynamicSDKManager.OnWalletConnected -= OnWalletConnected;
        DynamicSDKManager.OnWalletDisconnected -= OnWalletDisconnected;
        DynamicSDKManager.OnUserAuthenticated -= OnUserAuthenticated;
        DynamicSDKManager.OnJwtTokenReceived -= OnJwtTokenReceived;
        DynamicSDKManager.OnSDKError -= OnSDKError;
        DynamicSDKManager.OnWebViewReady -= OnWebViewReady;
        DynamicSDKManager.OnWebViewClosed -= OnWebViewClosed;
        DynamicSDKManager.OnMessageSigned -= OnMessageSigned;
    }

    void Start()
    {
        m_manifest = Resources.Load<DynamicSDKManifest>("DynamicSDKManifest");
        if (m_manifest != null)
        {
            // simulate to set environmentId dynamically
            m_manifest.environmentId = RetrieveEnvironmentId();
        }

        m_sdk = DynamicSDKManager.Instance;
        ShowDynamicAuth();

        _ = LoadConfig();
    }

    /////////////////////////////////////////////////

    void CopyJwt()
    {
        var jwt = JwtResult.text;
        if (!string.IsNullOrEmpty(jwt))
        {
            GUIUtility.systemCopyBuffer = jwt;
        }
    }

    void SignMessage()
    {
        if (!m_sdk.IsWalletConnected)
        {
            return;
        }

        var message = MessageInput.text;
        if (!string.IsNullOrEmpty(message))
        {
            Signature.text = default;
            if (m_shrinkWebViewWhenSigning)
            {
                m_sdk.WebView.ShrinkWebView();
            }
            m_sdk.SignMessage(message, isSuiTransaction: true);
        }
    }

    private string RetrieveEnvironmentId()
    {
        var useStaging = PlayerPrefs.GetInt("use-staging", 0) == 1;
        return useStaging ? "c1eed653-f1d1-4615-9fa7-181ad415f209" : "c1564a11-63ec-4236-8414-ec7972cc767f";
    }

    private void ShowDynamicAuth()
    {
        if (!m_sdk.IsInitialized)
        {
            m_sdk.InitializeSDK();
        }

        if (!m_sdk.IsWebViewReady)
        {
            m_waitingWebviewReady = true;
            return;
        }

        if (!m_sdk.IsWalletConnected)
        {
            m_isAuthenticating = true;
            m_authResult = default;

            m_sdk.ConnectWallet();
        }
        else
        {
            _ = GetJWT(delay: 0);
        }
    }

    private void Disconnect()
    {
        if (!m_sdk.IsWalletConnected)
        {
            return;
        }

        m_authResult = default;
        m_sdk.DisconnectWallet();
    }

    //////////////////////////////////////////////////

    private void OnWalletConnected(string walletAddress)
    {
        Debug.Log($"[DynamicTest] Wallet connected: {walletAddress}");
        m_authResult = 1;
    }

    private void OnWalletDisconnected()
    {
        Debug.Log($"[DynamicTest] Wallet disconnected");

        JwtResult.text = default;
    }

    private void OnUserAuthenticated(UserInfo userInfo)
    {
        Debug.Log($"[DynamicTest] User authenticated: {userInfo.email}");
        m_authResult = 2;
    }

    private void OnJwtTokenReceived(JwtTokenResponseMessage jwtToken)
    {
        var jwt = jwtToken.data.token;
        JwtResult.text = jwt;

        Debug.Log($"[DynamicTest] JWT token received: {jwt}");
    }

    private void OnSDKError(string error)
    {
        Debug.Log($"[DynamicTest] SDK Error: {error}");
        // m_authResult = -1;
    }

    private void OnWebViewReady()
    {
        Debug.Log("[DynamicTest] WebView ready");

        if (m_waitingWebviewReady)
        {
            m_waitingWebviewReady = false;

            if (!m_sdk.IsWalletConnected)
            {
                m_isAuthenticating = true;
                m_authResult = default;

                m_sdk.ConnectWallet();
            }
            else
            {
                _ = GetJWT(delay: 0);
            }
        }
    }

    private void OnWebViewClosed()
    {
        Debug.Log("[DynamicTest] WebView closed");

        if (m_isAuthenticating)
        {
            m_isAuthenticating = false;
        }

        if (m_authResult > 0 && string.IsNullOrEmpty(JwtResult.text))
        {
            _ = GetJWT(delay: 0.25f);
        }
    }

    private void OnMessageSigned(string signature)
    {
        Signature.text = signature;
        GUIUtility.systemCopyBuffer = signature;

        if (m_shrinkWebViewWhenSigning)
        {
            _ = ExpandWebView(0.5f);
        }
    }

    async Awaitable ExpandWebView(float delay)
    {
        await Awaitable.WaitForSecondsAsync(delay);
        m_sdk.WebView.ExpandWebView();
    }

    async Awaitable GetJWT(float delay)
    {
        var cancelToken = destroyCancellationToken;
        if (delay > 0)
        {
            await Awaitable.WaitForSecondsAsync(delay);
            if (cancelToken.IsCancellationRequested)
            {
                return;
            }
        }

        DynamicSDKManager.Instance.GetJwtToken();
    }

    private async Awaitable LoadConfig()
    {
        Debug.Log($"Download Config");
        var url = $"https://remote-config.game.claynosaurz.com/configs/claynosaurz:android:googleplay/beta";

        using var request = UnityWebRequest.Get(url);
        request.timeout = 20;
        // request.certificateHandler = new CustomCertificateHandler();

        await request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"Config data: {request.downloadHandler.text}");
        }
        else if (request.result == UnityWebRequest.Result.ConnectionError)
        {
            Debug.LogWarning($"Config connection error: {request.error}");
        }
        else if (request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogWarning($"Config protocol error: {request.error}");
        }
        else
        {
            Debug.LogWarning($"Config error: {request.error}");
        }
    }
}
