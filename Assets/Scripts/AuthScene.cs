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

    float m_delayToGetJwt = -1f;

    bool m_waitingWebviewReady = false;
    bool m_shrinkWebViewWhenSigning = true;

    void Awake()
    {
        DynamicSDKManager.OnWalletConnected += OnWalletConnected;
        DynamicSDKManager.OnWalletDisconnected += OnWalletDisconnected;
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
            m_sdk.ConnectWallet();
        }
        else
        {
            GetJWT(delay: 0.1f);
        }
    }

    private void Disconnect()
    {
        if (!m_sdk.IsWalletConnected)
        {
            return;
        }

        m_sdk.DisconnectWallet();
    }

    //////////////////////////////////////////////////

    private void OnWalletConnected(string walletAddress)
    {
        Debug.Log($"[DynamicTest] Wallet connected: {walletAddress}");
        if (m_sdk.IsWalletConnected && string.IsNullOrEmpty(JwtResult.text))
        {
            GetJWT(delay: 0.5f);
        }
    }

    private void OnWalletDisconnected()
    {
        Debug.Log($"[DynamicTest] Wallet disconnected");

        JwtResult.text = default;
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
                m_sdk.ConnectWallet();
            }
            else
            {
                GetJWT(delay: 0.1f);
            }
        }
    }

    private void OnWebViewClosed()
    {
        Debug.Log("[DynamicTest] WebView closed");

        if (m_sdk.IsWalletConnected && string.IsNullOrEmpty(JwtResult.text))
        {
            GetJWT(delay: 0.25f);
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

    void GetJWT(float delay)
    {
        m_delayToGetJwt = delay;
    }

    void Update()
    {
        if (m_delayToGetJwt >= 0)
        {
            m_delayToGetJwt -= Time.deltaTime;
            if (m_delayToGetJwt < 0)
            {
                m_sdk.GetJwtToken();
            }
        }
    }
}
