using System.Collections;
using DynamicSDK.Config;
using UnityEngine;
using DynamicSDK.Unity.Core;
using DynamicSDK.Unity.Messages;
using DynamicSDK.Unity.Messages.Auth;
using DynamicSDK.Unity.Messages.Wallet;
using DynamicSDK.Unity.Utils;
using Newtonsoft.Json;

/// <summary>
/// Refactored WebView connector using service-oriented architecture
/// Manages Dynamic SDK integration with improved separation of concerns
/// </summary>
public class WebViewConnector : MonoBehaviour
{
    [Header("SDK Configuration")]
    [SerializeField]
    private DynamicSDKConfig config;

    // Core service components - removed UIManager dependency
    private WebViewService webViewService;
    private MessageHandlerService messageHandler;

    // Current wallet state
    private string currentWalletAddress;
    private string currentWalletChain = "sui"; // Default to sui, but will be updated from wallet info

    // Request queue to prevent simultaneous WebView operations
    private System.Collections.Generic.Queue<System.Action> requestQueue = new System.Collections.Generic.Queue<System.Action>();
    private bool isProcessingRequest = false;

    // ============================================================================
    // EVENTS FOR DYNAMICSDKMANAGER
    // ============================================================================
    public System.Action<string> OnWalletConnected;
    public System.Action<UserInfo> OnUserAuthenticated;
    public System.Action<WalletCredential> OnWalletInfoUpdated;
    public System.Action OnWalletDisconnected;
    public System.Action<TransactionReceipt> OnTransactionReceived;
    public System.Action<string> OnMessageSigned;
    public System.Action<BalanceResponseData> OnBalanceUpdated;
    public System.Action<BalanceResponseData> OnWalletSwitched;
    public System.Action<BalanceResponseData> OnNetworkSwitched;
    public System.Action<WalletsResponseData> OnWalletsReceived;
    public System.Action<NetworksResponseData> OnNetworksReceived;
    public System.Action<string> OnError;
    public System.Action<JwtTokenResponseMessage> OnJwtTokenReceived;

    // ============================================================================
    // PUBLIC API METHODS FOR DYNAMICSDKMANAGER
    // ============================================================================

    /// <summary>
    /// Connect wallet - public method for DynamicSDKManager
    /// </summary>
    public void ConnectWallet() { ConnectWalletInternal(); }

    /// <summary>
    /// Disconnect wallet - public method for DynamicSDKManager
    /// </summary>
    public void DisconnectWallet() { DisconnectWalletInternal(); }

    /// <summary>
    /// Sign message - public method for DynamicSDKManager
    /// </summary>
    public void SignMessage(string message, bool isSuiTransaction) { SignMessageInternal(message, isSuiTransaction); }

    /// <summary>
    /// Send transaction - public method for DynamicSDKManager
    /// </summary>
    public void SendTransaction(string to, string value, string data = "", string network = "mainnet") { SendTransactionInternal(to, value, data, network); }

    /// <summary>
    /// Get balance - public method for DynamicSDKManager
    /// </summary>
    public void GetBalance() { GetBalanceInternal(); }

    /// <summary>
    /// Get wallets - public method for DynamicSDKManager
    /// </summary>
    public void GetWallets() { GetWalletsInternal(); }

    /// <summary>
    /// Get networks - public method for DynamicSDKManager
    /// </summary>
    public void GetNetworks() { GetNetworksInternal(); }

    /// <summary>
    /// Check connection status - public method for DynamicSDKManager
    /// </summary>
    public void CheckConnectionStatus()
    {
        // Implementation for checking connection status
        bool isConnected = !string.IsNullOrEmpty(currentWalletAddress);

        if (isConnected)
        {
            OnWalletConnected?.Invoke(currentWalletAddress);
        }
        else
        {
            OnWalletDisconnected?.Invoke();
        }
    }

    /// <summary>
    /// Open profile - public method for DynamicSDKManager
    /// </summary>
    public void OpenProfile() { OpenProfileInternal(); }

    /// <summary>
    /// Get JWT token - public method for DynamicSDKManager
    /// </summary>
    public void GetJwtToken() { GetJwtTokenInternal(); }

    /// <summary>
    /// Switch wallet - public method for DynamicSDKManager
    /// </summary>
    public void SwitchWallet(string walletId) { SwitchWalletInternal(walletId); }

    /// <summary>
    /// Switch network - public method for DynamicSDKManager
    /// </summary>
    public void SwitchNetwork(string networkChainId) { SwitchNetworkInternal(networkChainId); }

    // ============================================================================
    // REQUEST QUEUE MANAGEMENT
    // ============================================================================

    /// <summary>
    /// Queue a WebView request to prevent simultaneous operations
    /// </summary>
    private void QueueWebViewRequest(System.Action requestAction)
    {
        requestQueue.Enqueue(requestAction);
        ProcessRequestQueue();
    }

    /// <summary>
    /// Process the next request in the queue
    /// </summary>
    private void ProcessRequestQueue()
    {
        if (isProcessingRequest || requestQueue.Count == 0)
            return;

        // Check if WebViewService is ready before processing
        if (this == null || webViewService == null)
        {
            Debug.LogWarning("[WebViewConnector] WebView service not ready, clearing request queue");
            requestQueue.Clear();
            isProcessingRequest = false;
            return;
        }

        isProcessingRequest = true;
        var nextRequest = requestQueue.Dequeue();

        Debug.Log($"[WebViewConnector] Processing queued request. Remaining in queue: {requestQueue.Count}");

        // Execute the request
        nextRequest?.Invoke();
    }

    // ============================================================================
    // LIFECYCLE METHODS
    // ============================================================================

    void Awake()
    {
        InitializeServices();
        SetupEventHandlers();
        
        // Add DeepLinkHandler for OAuth callbacks
        if (GetComponent<DeepLinkHandler>() == null)
        {
            gameObject.AddComponent<DeepLinkHandler>();
            
            if (config != null && config.enableDebugLogs)
            {
                Debug.Log("[WebViewConnector] DeepLinkHandler added");
            }
        }
    }

    private void InitializeServices()
    {
        // Load configuration
        if (config != null)
        {
            DynamicSDKConfig.LoadFromAsset(config);
        }

        // Initialize services
        webViewService = GetComponent<WebViewService>();

        if (webViewService == null)
        {
            webViewService = gameObject.AddComponent<WebViewService>();
        }

        messageHandler = new MessageHandlerService();

        // Subscribe to WebView closed event to process next request
        if (webViewService != null)
        {
            webViewService.OnWebViewClosed += OnWebViewClosed;
        }
    }

    /// <summary>
    /// Called when WebView is closed/hidden - process next request in queue
    /// </summary>
    private void OnWebViewClosed()
    {
        // Process next request directly without coroutine delay
        isProcessingRequest = false;

        // Use Invoke to add a small delay without coroutines
        if (this != null && gameObject != null && gameObject.activeInHierarchy)
        {
            Invoke(nameof(ProcessRequestQueue), 0.5f);
        }
        else
        {
            // If GameObject is inactive, process immediately
            ProcessRequestQueue();
        }
    }

    private void SetupEventHandlers()
    {
        // WebView Service events
        if (webViewService != null)
        {
            webViewService.OnMessageReceived += messageHandler.ProcessMessage;
        }

        // Message Handler events
        SetupMessageHandlerEvents();
    }

    private void SetupMessageHandlerEvents()
    {
        messageHandler.OnAuthSuccess += HandleAuthSuccess;
        messageHandler.OnAuthFailed += HandleAuthFailed;
        messageHandler.OnLoggedOut += HandleLoggedOut;
        messageHandler.OnAuthenticatedUser += HandleAuthenticatedUser;
        messageHandler.OnJwtTokenResponse += HandleJwtTokenResponse;

        messageHandler.OnBalanceResponse += HandleBalanceResponse;
        messageHandler.OnWalletSwitched += HandleWalletSwitched;
        messageHandler.OnNetworkSwitched += HandleNetworkSwitched;
        messageHandler.OnSignMessageResponse += HandleSignMessageResponse;
        messageHandler.OnTransactionResponse += HandleTransactionResponse;
        messageHandler.OnWalletConnected += HandleWalletConnected;
        messageHandler.OnWalletDisconnected += HandleWalletDisconnected;
        messageHandler.OnWalletError += HandleWalletError;
        messageHandler.OnWalletsResponse += HandleWalletsResponse;
        messageHandler.OnNetworksResponse += HandleNetworksResponse;
    }

    // ============================================================================
    // AUTH MESSAGE HANDLERS
    // ============================================================================

    private void HandleAuthSuccess(AuthSuccessMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Auth success message data is null");

            return;
        }

        Debug.Log($"[WebViewConnector] Auth Success - User: {message.data.user?.name}, Provider: {message.data.provider}");

        // Display user info if available
        if (message.data.user != null)
        {
            // Fire user authenticated event
            OnUserAuthenticated?.Invoke(message.data.user);
        }

        // Display wallet info if available
        if (message.data.primaryWallet != null)
        {
            UpdateWalletInfo(message.data.primaryWallet);

            // Fire wallet connected event for scene transition
            if (!string.IsNullOrEmpty(currentWalletAddress))
            {
                OnWalletConnected?.Invoke(currentWalletAddress);
            }
        }

        // Store session token if needed
        // PlayerPrefs.SetString("SessionToken", message.data.sessionToken);

        // Hide webview
        webViewService?.HideWithAnimation();
    }

    private void HandleAuthFailed(AuthFailedMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Auth failed message data is null");

            return;
        }

        Debug.LogError($"[WebViewConnector] Auth Failed - Error: {message.data.error}, Code: {message.data.errorCode}");

        // Hide webview
        webViewService?.HideWithAnimation();
    }

    private void HandleLoggedOut(LoggedOutMessage message)
    {
        if (message?.data != null)
        {
            Debug.Log($"[WebViewConnector] User logged out - Success: {message.data.success}, Timestamp: {message.data.timestamp}");
        }
        else
        {
            Debug.Log("[WebViewConnector] User logged out");
        }

        // Clear wallet info and reset state
        ClearWalletInfo();

        // Clear session data
        // PlayerPrefs.DeleteKey("SessionToken");

        // Fire disconnect event for DynamicSDKManager
        OnWalletDisconnected?.Invoke();

        // Hide webview
        webViewService?.HideWithAnimation();
    }

    private void HandleAuthenticatedUser(HandleAuthenticatedUserMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Authenticated user message data is null");

            return;
        }

        Debug.Log($"[WebViewConnector] Authenticated User - User: {message.data.user?.name}");

        // Display user info if available
        if (message.data.user != null)
        {
            // Fire user authenticated event
            OnUserAuthenticated?.Invoke(message.data.user);
        }

        // Display wallet info if available
        if (message.data.wallets != null && message.data.wallets.Length > 0)
        {
            var wallet = message.data.wallets[0];
            UpdateWalletInfo(wallet);

            // Fire wallet connected event for scene transition
            if (!string.IsNullOrEmpty(currentWalletAddress))
            {
                OnWalletConnected?.Invoke(currentWalletAddress);
            }
        }

        // Store session token
        // PlayerPrefs.SetString("SessionToken", message.data.sessionToken);

        webViewService?.HideWithAnimation();
    }

    private void HandleJwtTokenResponse(JwtTokenResponseMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] JWT token response message data is null");
            OnError?.Invoke("Failed to get JWT token: Invalid response");
            return;
        }

        Debug.Log($"[WebViewConnector] JWT Token Response - Token: {message.data.token?.Substring(0, 20)}..., UserId: {message.data.userId}, Email: {message.data.email}");

        // Fire JWT token received event
        OnJwtTokenReceived?.Invoke(message);

        // Hide webview
        webViewService?.HideWithAnimation();
    }

    // ============================================================================
    // UTILITY METHODS
    // ============================================================================

    private void UpdateWalletInfo(WalletCredential wallet)
    {
        if (wallet == null) return;

        currentWalletAddress = wallet.address;

        // Update current wallet chain from wallet info
        if (!string.IsNullOrEmpty(wallet.chain))
        {
            currentWalletChain = wallet.chain.ToLower();
        }

        Debug.Log($"[WebViewConnector] Wallet Info - Address: {FormatAddress(wallet.address)}, Chain: {wallet.chain}, Balance: {wallet.balance}");

        // Fire wallet info updated event
        OnWalletInfoUpdated?.Invoke(wallet);
    }

    private void ClearWalletInfo()
    {
        currentWalletAddress = null;
        currentWalletChain = "sui"; // Reset to default
        Debug.Log("[WebViewConnector] Wallet info cleared");
    }

    // ============================================================================
    // WALLET MESSAGE HANDLERS
    // ============================================================================



    private void HandleBalanceResponse(BalanceResponseMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Balance response message data is null");
            OnError?.Invoke("Failed to get balance: Invalid response");

            return;
        }

        if (message.data.success)
        {
            Debug.Log($"[WebViewConnector] Balance Response - Address: {message.data.walletAddress}, Balance: {message.data.balance} {message.data.symbol}");

            // Update current wallet address from balance response
            currentWalletAddress = message.data.walletAddress;

            // Update current wallet chain from balance response
            if (!string.IsNullOrEmpty(message.data.chain))
            {
                currentWalletChain = message.data.chain.ToLower();
            }

            // Get correct symbol for the chain
            string correctSymbol = GetCorrectSymbolForChain(message.data.chain, message.data.symbol);

            // If we have chain info in balance response, create/update wallet info
            if (!string.IsNullOrEmpty(message.data.chain))
            {
                var walletInfo = new WalletCredential
                {
                    address = message.data.walletAddress,
                    chain = message.data.chain,
                    balance = message.data.balance,
                    network = message.data.network,
                    symbol = correctSymbol,
                    decimals = message.data.decimals,
                };

                // Fire wallet info updated event
                OnWalletInfoUpdated?.Invoke(walletInfo);
            }

            // Fire balance updated event with correct symbol based on chain
            OnBalanceUpdated?.Invoke(message.data);
        }
        else
        {
            Debug.LogError($"[WebViewConnector] Balance request failed: {message.data.error}");
            OnError?.Invoke($"Failed to get balance: {message.data.error}");
        }
        webViewService?.HideWithAnimation();
    }
    private void HandleWalletSwitched(BalanceResponseMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Balance response message data is null");
            OnError?.Invoke("Failed to get balance: Invalid response");

            return;
        }

        if (message.data.success)
        {
            Debug.Log($"[WebViewConnector] Balance Response - Address: {message.data.walletAddress}, Balance: {message.data.balance} {message.data.symbol}");

            // Update current wallet address from balance response
            currentWalletAddress = message.data.walletAddress;

            // Update current wallet chain from balance response
            if (!string.IsNullOrEmpty(message.data.chain))
            {
                currentWalletChain = message.data.chain.ToLower();
            }

            // Get correct symbol for the chain
            string correctSymbol = GetCorrectSymbolForChain(message.data.chain, message.data.symbol);

            // If we have chain info in balance response, create/update wallet info
            if (!string.IsNullOrEmpty(message.data.chain))
            {
                var walletInfo = new WalletCredential
                {
                    address = message.data.walletAddress,
                    chain = message.data.chain,
                    network = message.data.network,
                    balance = message.data.balance,
                    symbol = correctSymbol,
                    decimals = message.data.decimals,
                };

                // Fire wallet info updated event
                OnWalletInfoUpdated?.Invoke(walletInfo);
            }

            // Fire balance updated event with correct symbol based on chain
            OnWalletSwitched?.Invoke(message.data);
        }
        else
        {
            Debug.LogError($"[WebViewConnector] Balance request failed: {message.data.error}");
            OnError?.Invoke($"Failed to get balance: {message.data.error}");
        }
        webViewService?.HideWithAnimation();
    }

    private void HandleNetworkSwitched(BalanceResponseMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Balance response message data is null");
            OnError?.Invoke("Failed to get balance: Invalid response");

            return;
        }

        if (message.data.success)
        {
            Debug.Log($"[WebViewConnector] Balance Response - Address: {message.data.walletAddress}, Balance: {message.data.balance} {message.data.symbol}");

            // Update current wallet address from balance response
            currentWalletAddress = message.data.walletAddress;

            // Update current wallet chain from balance response
            if (!string.IsNullOrEmpty(message.data.chain))
            {
                currentWalletChain = message.data.chain.ToLower();
            }

            // Get correct symbol for the chain
            string correctSymbol = GetCorrectSymbolForChain(message.data.chain, message.data.symbol);

            // If we have chain info in balance response, create/update wallet info
            if (!string.IsNullOrEmpty(message.data.chain))
            {
                var walletInfo = new WalletCredential
                {
                    address = message.data.walletAddress,
                    chain = message.data.chain,
                    network = message.data.network,
                    balance = message.data.balance,
                    symbol = correctSymbol,
                    decimals = message.data.decimals,
                };

                // Fire wallet info updated event
                OnWalletInfoUpdated?.Invoke(walletInfo);
            }

            // Fire balance updated event with correct symbol based on chain
            OnNetworkSwitched?.Invoke(message.data);
        }
        else
        {
            Debug.LogError($"[WebViewConnector] Balance request failed: {message.data.error}");
            OnError?.Invoke($"Failed to get balance: {message.data.error}");
        }
        webViewService?.HideWithAnimation();
    }

    private void HandleSignMessageResponse(SignMessageResponseMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Sign message response data is null");
            OnError?.Invoke("Failed to sign message: Invalid response");

            return;
        }

        if (message.data.success)
        {
            Debug.Log($"[WebViewConnector] Message signed successfully: {message.data.signature}");
            Debug.Log($"[WebViewConnector] Original message: {message.data.message}");
            // Fire message signed event
            OnMessageSigned?.Invoke(message.data.signature);
        }
        else
        {
            Debug.LogError($"[WebViewConnector] Sign message failed: {message.data.error}");
            OnError?.Invoke($"Sign failed: {message.data.error}");
        }

        // On android, when disable ShowWebViewWhenSigningMessage, OnWebViewClosed is not triggered.
        // Maybe because the webview rect is set to zero. Unset isProcessingRequest to not block later request.
        if (!DynamicSDKManager.Instance.Config.ShowWebViewWhenSigningMessage &&
            isProcessingRequest)
        {
            isProcessingRequest = false;
        }

        webViewService?.HideWithAnimation(() =>
        {
            if(!DynamicSDKManager.Instance.Config.ShowWebViewWhenSigningMessage)
                webViewService?.ExpandWebView();
        });
    }

    private void HandleTransactionResponse(TransactionResponseMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Transaction response data is null");
            OnError?.Invoke("Failed to send transaction: Invalid response");

            return;
        }

        if (message.data.success)
        {
            var transactionReceipt = new TransactionReceipt()
            {
                TransactionHash = message.data.transactionHash,
                TransactionBytes = message.data.transactionBytes,
                TransactionSignature = message.data.transactionSignature
            };
            Debug.Log($"[WebViewConnector] Transaction successful: {transactionReceipt}");

            OnTransactionReceived?.Invoke(transactionReceipt);
        }
        else
        {
            Debug.LogError($"[WebViewConnector] Transaction failed: {message.data.error}");
            OnError?.Invoke($"Transaction failed: {message.data.error}");
        }

        // Hide webview after transaction operation
        webViewService?.HideWithAnimation();
    }

    private void HandleWalletConnected(WalletConnectedMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Wallet connected message data is null");
            OnError?.Invoke("Wallet connection failed: Invalid response");

            return;
        }

        if (message.data.success)
        {
            Debug.Log($"[WebViewConnector] Wallet Connected - Address: {message.data.wallet?.address}, Chain: {message.data.wallet?.chain}");

            if (message.data.wallet != null)
            {
                // Update wallet info
                UpdateWalletInfo(message.data.wallet);

                // Fire wallet connected event
                OnWalletConnected?.Invoke(message.data.wallet.address);
            }

            // Hide webview
            webViewService?.HideWithAnimation();
        }
        else
        {
            OnError?.Invoke("Wallet connection failed");
        }
    }

    private void HandleWalletDisconnected(WalletDisconnectedMessage message)
    {
        Debug.Log($"[WebViewConnector] Wallet Disconnected - Address: {message?.data?.walletAddress}, Reason: {message?.data?.reason}");

        // Clear wallet info and update state
        ClearWalletInfo();

        // Fire wallet disconnected event
        OnWalletDisconnected?.Invoke();

        // Hide webview
        webViewService?.HideWithAnimation();
    }

    private void HandleWalletError(WalletErrorMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Wallet error message data is null");

            return;
        }

        Debug.LogError($"[WebViewConnector] Wallet Error: {message.data.error}");

        // Fire error event
        OnError?.Invoke($"Wallet error: {message.data.error}");
    }

    private void HandleWalletsResponse(WalletsResponseMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Wallets response message data is null");
            OnError?.Invoke("Failed to get wallets: Invalid response");
            return;
        }

        if (message.data.success)
        {
            Debug.Log($"[WebViewConnector] Wallets Response - Found {message.data.wallets?.Length ?? 0} wallets");

            // Fire wallets received event
            OnWalletsReceived?.Invoke(message.data);
        }
        else
        {
            Debug.LogError($"[WebViewConnector] Get wallets failed: {message.data.error}");
            OnError?.Invoke($"Failed to get wallets: {message.data.error}");
        }

        // Hide webview after wallets operation
        webViewService?.HideWithAnimation();
    }

    private void HandleNetworksResponse(NetworksResponseMessage message)
    {
        if (message?.data == null)
        {
            Debug.LogError("[WebViewConnector] Networks response message data is null");
            OnError?.Invoke("Failed to get networks: Invalid response");
            return;
        }

        if (message.data.success)
        {
            Debug.Log($"[WebViewConnector] Networks Response - Found {message.data.networks?.Length ?? 0} networks");
            // Fire networks received event
            OnNetworksReceived?.Invoke(message.data);
        }
        else
        {
            Debug.LogError($"[WebViewConnector] Get networks failed: {message.data.error}");
            OnError?.Invoke($"Failed to get networks: {message.data.error}");
        }

        // Hide webview after networks operation
        webViewService?.HideWithAnimation();
    }

    // ============================================================================
    // BUTTON HANDLERS
    // ============================================================================

    private void ConnectWalletInternal()
    {
        if (!string.IsNullOrEmpty(currentWalletAddress))
        {
            Debug.Log("[WebViewConnector] Wallet already connected");

            return;
        }

        Debug.Log("[WebViewConnector] Connect wallet requested");

        isProcessingRequest = false; // Reset processing state

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Make sure the WebView is open and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(SendConnectWalletRequest, 1.0f);
        });
    }

    private void SendConnectWalletRequest()
    {
        string message = RequestBuilder.BuildConnectWalletRequest();
        webViewService?.SendMessage(message);
    }

    private void DisconnectWalletInternal()
    {
        if (string.IsNullOrEmpty(currentWalletAddress))
        {
            Debug.Log("[WebViewConnector] No wallet connected");

            return;
        }

        Debug.Log("[WebViewConnector] Disconnect wallet requested");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send disconnect request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(SendDisconnectRequest, 1.0f);
        });
    }

    private void SendDisconnectRequest()
    {
        string message = RequestBuilder.BuildDisconnectRequest();
        webViewService?.SendMessage(message);

        Debug.Log($"[WebViewConnector] Sent disconnect request: {message}");

        // Update Unity-side state immediately (you can wait for Web feedback if preferred)
        ClearWalletInfo();

        // Fire disconnect event for DynamicSDKManager
        OnWalletDisconnected?.Invoke();
    }

    private void SignMessageInternal()
    {
        // This method would need to get message from external source
        // since we removed UIManager dependency
        Debug.LogWarning("[WebViewConnector] SignMessageInternal() called without message parameter");
    }

    private void SignMessageInternal(string message, bool isSuiTransaction)
    {
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogError("[WebViewConnector] Message cannot be empty");
            OnError?.Invoke("Please enter a message to sign");

            return;
        }

        // Check if wallet is connected
        if (string.IsNullOrEmpty(currentWalletAddress))
        {
            Debug.LogError("[WebViewConnector] No wallet connected");
            OnError?.Invoke("No wallet connected");

            return;
        }

        Debug.Log($"[WebViewConnector] Sign message requested: {message}");

        QueueWebViewRequest(() =>
        {
            
            if(!DynamicSDKManager.Instance.Config.ShowWebViewWhenSigningMessage)
                webViewService?.ShrinkWebView();
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendSignMessageRequest(message, isSuiTransaction), 1.0f);
        });
    }

    private void SendSignMessageRequest(string message, bool isSuiTransaction)
    {
        if (!RequestBuilder.Validator.IsValidMessage(message))
        {
            OnError?.Invoke("Invalid message");

            return;
        }

        string request = RequestBuilder.BuildSignMessageRequest(currentWalletAddress, message, isSuiTransaction);
     
        
        webViewService?.SendMessage(request);
        

        Debug.Log($"[WebViewConnector] Sent sign message request: {request}");
    }

    private void SendTransactionInternal(string to, string value, string data = "", string network = "mainnet")
    {
        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(value))
        {
            Debug.LogError("[WebViewConnector] Invalid transaction parameters");
            OnError?.Invoke("Invalid transaction parameters");

            return;
        }

        // Check if wallet is connected
        if (string.IsNullOrEmpty(currentWalletAddress))
        {
            Debug.LogError("[WebViewConnector] No wallet connected");
            OnError?.Invoke("No wallet connected");

            return;
        }

        // Validate inputs
        if (!RequestBuilder.Validator.IsValidAddress(to))
        {
            Debug.LogError("[WebViewConnector] Invalid recipient address format");
            OnError?.Invoke("Invalid recipient address");

            return;
        }

        if (!RequestBuilder.Validator.IsValidAmount(value))
        {
            Debug.LogError("[WebViewConnector] Invalid amount format");
            OnError?.Invoke("Invalid amount");

            return;
        }

        Debug.Log($"[WebViewConnector] Send transaction requested: {value} to {to} on {network}");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendTransactionRequest(to, value, network), 1.0f);
        });
    }

    private void SendTransactionRequest(string toAddress, string amount, string network = "mainnet")
    {
        string request = RequestBuilder.BuildTransactionRequest(currentWalletAddress, toAddress, amount, currentWalletChain, network);
        webViewService?.SendMessage(request);

        Debug.Log($"[WebViewConnector] Sent transaction request: {request}");
    }

    private void GetBalanceInternal()
    {
        // Check if WebViewService is ready before proceeding
        if (this == null || webViewService == null)
        {
            Debug.LogWarning("[WebViewConnector] Cannot get balance - WebView service not ready");
            OnError?.Invoke("SDK not ready - please try again");
            return;
        }

        Debug.Log("[WebViewConnector] Getting wallet balance...");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendGetBalanceRequest(), 1.0f);
        });
    }

    private void SendGetBalanceRequest()
    {
        string request = RequestBuilder.BuildGetBalanceRequest();
        webViewService?.SendMessage(request);

        Debug.Log($"[WebViewConnector] Sent get balance request: {request}");
    }

    private void GetWalletsInternal()
    {
        // Check if WebViewService is ready before proceeding
        if (this == null || webViewService == null)
        {
            Debug.LogWarning("[WebViewConnector] Cannot get wallets - WebView service not ready");
            OnError?.Invoke("SDK not ready - please try again");
            return;
        }

        Debug.Log("[WebViewConnector] Getting available wallets...");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendGetWalletsRequest(), 1.0f);
        });
    }

    private void SendGetWalletsRequest()
    {
        string request = RequestBuilder.BuildGetWalletsRequest();
        webViewService?.SendMessage(request);

        Debug.Log($"[WebViewConnector] Sent get wallets request: {request}");
    }

    private void GetNetworksInternal()
    {
        // Check if WebViewService is ready before proceeding
        if (this == null || webViewService == null)
        {
            Debug.LogWarning("[WebViewConnector] Cannot get networks - WebView service not ready");
            OnError?.Invoke("SDK not ready - please try again");
            return;
        }

        Debug.Log("[WebViewConnector] Getting available networks...");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendGetNetworksRequest(), 1.0f);
        });
    }

    private void SendGetNetworksRequest()
    {
        string request = RequestBuilder.BuildGetNetworksRequest();
        webViewService?.SendMessage(request);

        Debug.Log($"[WebViewConnector] Sent get networks request: {request}");
    }

    // ============================================================================
    // UTILITY METHODS
    // ============================================================================

    private string FormatAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length <= 10)
            return address;

        return $"{address.Substring(0, 6)}...{address.Substring(address.Length - 4)}";
    }

    private string GetFullWalletAddress() { return currentWalletAddress; }

    /// <summary>
    /// Get correct symbol for a given chain, fallback to provided symbol if chain not recognized
    /// </summary>
    private string GetCorrectSymbolForChain(string chain, string fallbackSymbol)
    {
        if (string.IsNullOrEmpty(chain))
            return fallbackSymbol;

        // Map chains to their native symbols
        switch (chain.ToLower())
        {
            case "sui":
                return "SUI";
            case "ethereum":
            case "eth":
            case "evm":
                return "ETH";
            case "solana":
            case "sol":
                return "SOL";
            case "polygon":
            case "matic":
                return "MATIC";
            case "binance":
            case "bsc":
            case "bnb":
                return "BNB";
            case "avalanche":
            case "avax":
                return "AVAX";
            default:
                // Return original symbol if chain not recognized
                return fallbackSymbol ?? "TOKEN";
        }
    }

    void OnDestroy()
    {
        // Cancel any pending Invoke calls
        CancelInvoke();

        // Unsubscribe from events
        if (webViewService != null)
        {
            webViewService.OnWebViewClosed -= OnWebViewClosed;
        }

        // Clean up services
        webViewService?.CloseWebView();
    }

    private void OpenProfileInternal()
    {
        // Check if wallet is connected
        if (string.IsNullOrEmpty(currentWalletAddress))
        {
            Debug.LogError("[WebViewConnector] No wallet connected");
            OnError?.Invoke("No wallet connected");

            return;
        }

        Debug.Log("[WebViewConnector] Opening profile");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendOpenProfileRequest(), 1.0f);
        });
    }

    private void SendOpenProfileRequest()
    {
        string request = RequestBuilder.BuildOpenProfileRequest(currentWalletAddress);
        webViewService?.SendMessage(request);

        Debug.Log($"[WebViewConnector] Sent open profile request: {request}");
    }

    private void GetJwtTokenInternal()
    {
        Debug.Log("[WebViewConnector] Getting JWT token");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendGetJwtTokenRequest(), 1.0f);
        });
    }

    private void SendGetJwtTokenRequest()
    {
        string request = RequestBuilder.BuildGetJwtTokenRequest();
        webViewService?.SendMessage(request);

        Debug.Log($"[WebViewConnector] Sent get JWT token request: {request}");
    }

    private void SwitchWalletInternal(string walletId)
    {
        // Check if WebViewService is ready before proceeding
        if (this == null || webViewService == null)
        {
            Debug.LogWarning("[WebViewConnector] Cannot switch wallet - WebView service not ready");
            OnError?.Invoke("SDK not ready - please try again");
            return;
        }

        Debug.Log($"[WebViewConnector] Switching to wallet: {walletId}");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendSwitchWalletRequest(walletId), 1.0f);
        });
    }

    private void SendSwitchWalletRequest(string walletId)
    {
        string request = RequestBuilder.BuildSwitchWalletRequest(walletId);
        webViewService?.SendMessage(request);

        Debug.Log($"[WebViewConnector] Sent switch wallet request: {request}");
    }

    private void SwitchNetworkInternal(string networkChainId)
    {
        // Check if WebViewService is ready before proceeding
        if (this == null || webViewService == null)
        {
            Debug.LogWarning("[WebViewConnector] Cannot switch network - WebView service not ready");
            OnError?.Invoke("SDK not ready - please try again");
            return;
        }

        Debug.Log($"[WebViewConnector] Switching to network: {networkChainId}");

        // Queue this request to prevent conflicts with other WebView operations
        QueueWebViewRequest(() =>
        {
            // Open WebView if needed and send request
            webViewService?.OpenBottomSheet();
            webViewService?.RetryWithDelay(() => SendSwitchNetworkRequest(networkChainId), 1.0f);
        });
    }

    private void SendSwitchNetworkRequest(string networkChainId)
    {
        string request = RequestBuilder.BuildSwitchNetworkRequest(networkChainId);
        webViewService?.SendMessage(request);

        Debug.Log($"[WebViewConnector] Sent switch network request: {request}");
    }
}