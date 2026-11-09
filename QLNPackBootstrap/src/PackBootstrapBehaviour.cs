using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace QLNPackBootstrap
{
    public sealed class PackBootstrapBehaviour : MonoBehaviour
    {
        private enum UiState
        {
            Hidden,
            Checking,
            NoUpdate,
            UpdateAvailable,
            Downloading,
            Verifying,
            Extracting,
            Staged,
            Error,
            ConfigError
        }

        private PackConfig _config;
        private PackManifest _manifest;
        private PackState _state;
        private PackPending _pending;
        private List<ModChange> _changes;

        private UiState _ui = UiState.Hidden;
        private string _error;
        private string _signatureNote;
        private bool _unsigned;
        private bool _checking;
        private bool _dismissed;
        private bool _mainMenuOpen;
        private float _createdAt;
        private bool _autoCheckDone;
        private float _autoHideAt;
        private float _autoRestartAt;
        private bool _autoRestartScheduled;

        private long _totalBytes;
        private float _progress;
        private string _currentName;
        private string _stageRoot;
        private bool _cancelRequested;

        private List<DownloadJob> _jobs;
        private int _completedJobs;
        private int _maxParallelDownloads = 4;
        private Vector2 _downloadScroll;

        private List<PackProfile> _packs = new List<PackProfile>();
        private PackProfile _activePack;
        private bool _packIndexLoaded;
        private bool _forcePackIndexRefresh;
        private bool _switchingPack;
        private Vector2 _packScroll;

        private Vector2 _scroll;
        private GUIStyle _wrap;
        private GUIStyle _title;
        private GUIStyle _small;
        private GUIStyle _button;
        private GUIStyle _tab;
        private GUIStyle _tabActive;
        private GUIStyle _menuButton;
        private GUIStyle _windowStyle;
        private Texture2D _panelTex;
        private Texture2D _buttonTex;
        private Texture2D _buttonHoverTex;
        private Texture2D _buttonActiveTex;
        private Texture2D _accentTex;
        private bool _stylesReady;
        private float _uiScale = 1f;

        internal void Initialize(PackConfig config)
        {
            _config = config ?? new PackConfig();
            _packs = new List<PackProfile>(_config.Packs);

            PackState state = PackInstaller.LoadState();
            string activeId = !string.IsNullOrEmpty(state.ActivePackId)
                ? state.ActivePackId
                : _config.DefaultPackId;
            _activePack = FindPack(activeId);

            if (_activePack == null &&
                (string.IsNullOrEmpty(_config.PackIndexUrl) || string.IsNullOrEmpty(state.ActivePackId)))
            {
                _activePack = _config.DefaultPack;
            }

            _createdAt = Time.realtimeSinceStartup;
            PackLogger.Info("Bootstrap behaviour initialized. Active pack=" +
                            (_activePack == null ? "(none)" : _activePack.Id));
            PackLogger.Info("Install target: " + PackPaths.ModsRoot);
        }

        private PackProfile FindPack(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            foreach (PackProfile profile in _packs)
            {
                if (string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return _config != null ? _config.FindPack(id) : null;
        }

        private void RefreshPackList()
        {
            _forcePackIndexRefresh = true;
            BeginCheck(true);
        }

        private IEnumerator RefreshPackIndexRoutine()
        {
            if (_config == null || string.IsNullOrWhiteSpace(_config.PackIndexUrl))
            {
                _packIndexLoaded = true;
                yield break;
            }

            Uri indexUri;
            if (!Uri.TryCreate(_config.PackIndexUrl, UriKind.Absolute, out indexUri))
            {
                PackLogger.Warn("PackIndexUrl is not a valid absolute URL.");
                _packIndexLoaded = true;
                yield break;
            }

            byte[] data = null;
            string error = null;
            yield return FetchBytes(
                PackUrls.WithToken(_config.PackIndexUrl, _config.AccessToken),
                2 * 1024 * 1024,
                bytes => data = bytes,
                message => error = message);

            if (error != null)
            {
                PackLogger.Warn("Could not load the remote pack index: " + error);
                _packIndexLoaded = true;
                yield break;
            }

            try
            {
                string json = Encoding.UTF8.GetString(data).TrimStart('\uFEFF');
                PackIndex index = JsonConvert.DeserializeObject<PackIndex>(json);
                MergePackIndex(index);
            }
            catch (Exception exception)
            {
                PackLogger.Warn("Could not parse the remote pack index: " + exception.Message);
            }

            _packIndexLoaded = true;
        }

        private void MergePackIndex(PackIndex index)
        {
            if (index == null || index.Packs == null)
            {
                return;
            }

            int added = 0;
            foreach (PackProfile remote in index.Packs)
            {
                if (remote == null || !IsValidPackId(remote.Id))
                {
                    continue;
                }

                if (remote.IsVanilla)
                {
                    continue;
                }

                if (FindPack(remote.Id) != null)
                {
                    // Local configuration wins over a remote entry with the same id.
                    continue;
                }

                if (string.IsNullOrWhiteSpace(remote.Name))
                {
                    remote.Name = remote.Id;
                }

                if (!remote.IsVanilla && !IsUsableManifestUrl(remote.ManifestUrl))
                {
                    PackLogger.Warn("Ignoring pack '" + remote.Id + "': invalid manifest URL.");
                    continue;
                }

                remote.IsRemote = true;
                _packs.Add(remote);

                if (_config != null && _config.FindPack(remote.Id) == null)
                {
                    _config.Packs.Add(remote);
                }

                added++;
            }

            if (added > 0)
            {
                PackLogger.Info("Added " + added + " pack(s) from the remote index.");
            }

            if (_activePack == null)
            {
                _activePack = _config.DefaultPack;
            }
        }

        private static bool IsValidPackId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            {
                return false;
            }

            foreach (char c in id)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsUsableManifestUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttps ||
                   (uri.Scheme == Uri.UriSchemeHttp && _config != null && _config.AllowInsecureHttp);
        }

        public void OnMainMenuOpened()
        {
            _mainMenuOpen = true;

            if (_config == null || !_config.CheckOnMainMenu || _dismissed || _ui != UiState.Hidden)
            {
                return;
            }

            _autoCheckDone = true;
            BeginCheck(false);
        }

        private void Update()
        {
            if (_mainMenuOpen &&
                GameManager.Instance != null &&
                GameManager.Instance.World != null)
            {
                _mainMenuOpen = false;
            }

            if (_autoRestartScheduled &&
                _ui == UiState.Staged &&
                Time.realtimeSinceStartup >= _autoRestartAt)
            {
                if (GameManager.Instance == null || GameManager.Instance.World == null)
                {
                    _autoRestartScheduled = false;
                    StartUpdaterAndQuit();
                    return;
                }

                // Do not quit out from under a loaded world. Try again shortly.
                _autoRestartAt = Time.realtimeSinceStartup + 5f;
            }

            if (_ui == UiState.NoUpdate && Time.realtimeSinceStartup >= _autoHideAt)
            {
                _ui = UiState.Hidden;
            }

            if (_autoCheckDone || _config == null)
            {
                return;
            }

            if (!_config.CheckOnMainMenu || _dismissed)
            {
                _autoCheckDone = true;
                return;
            }

            if (Time.realtimeSinceStartup - _createdAt < 5f)
            {
                return;
            }

            if (GameManager.Instance != null && GameManager.Instance.World != null)
            {
                _autoCheckDone = true;
                return;
            }

            _mainMenuOpen = true;
            _autoCheckDone = true;
            BeginCheck(false);
        }

        private void OnDestroy()
        {
            DestroyTexture(_panelTex);
            DestroyTexture(_buttonTex);
            DestroyTexture(_buttonHoverTex);
            DestroyTexture(_buttonActiveTex);
            DestroyTexture(_accentTex);

            if (PackBootstrapMod.Behaviour == this)
            {
                PackBootstrapMod.Behaviour = null;
            }
        }

        // ------------------------------------------------------------------
        // Check
        // ------------------------------------------------------------------

        private void BeginCheck(bool force)
        {
            if (_checking)
            {
                return;
            }

            if (force)
            {
                _dismissed = false;
            }

            _checking = true;
            StartCoroutine(CheckRoutine());
        }

        private IEnumerator CheckRoutine()
        {
            _ui = UiState.Checking;
            _error = null;
            _signatureNote = null;
            _unsigned = false;
            _pending = null;
            _changes = null;
            _switchingPack = false;

            if (GameManager.IsDedicatedServer)
            {
                _ui = UiState.Hidden;
                _checking = false;
                yield break;
            }

            string modsRootError;
            if (!ValidateModsRoot(out modsRootError))
            {
                _ui = UiState.ConfigError;
                _error = modsRootError;
                _checking = false;
                yield break;
            }

            if (File.Exists(PackPaths.PendingFile))
            {
                _pending = PackPending.Load(PackPaths.PendingFile);
                if (_pending == null)
                {
                    _ui = UiState.Error;
                    _error = "The staged update file is invalid. Delete " + PackPaths.PendingFile + " and recheck.";
                    _checking = false;
                    yield break;
                }

                _ui = UiState.Staged;
                if (_config != null &&
                    _config.AutoRestart &&
                    !File.Exists(PackPaths.UpdaterFailedFile))
                {
                    ScheduleAutoRestart();
                }

                _checking = false;
                yield break;
            }

            if (_config == null)
            {
                _ui = UiState.ConfigError;
                _error = "Configuration was not loaded.";
                _checking = false;
                yield break;
            }

            _state = PackInstaller.LoadState();

            if (_activePack == null)
            {
                _activePack = FindPack(_state.ActivePackId) ?? _config.DefaultPack;
            }

            if (!_packIndexLoaded || _forcePackIndexRefresh)
            {
                yield return RefreshPackIndexRoutine();
                _forcePackIndexRefresh = false;

                if (_activePack == null)
                {
                    _activePack = FindPack(_state.ActivePackId) ?? _config.DefaultPack;
                }
            }

            if (_activePack == null)
            {
                _ui = UiState.ConfigError;
                _error = "No mod pack profiles are configured. Edit " + PackPaths.ConfigFile + ".";
                _checking = false;
                yield break;
            }

            if (_activePack.IsVanilla)
            {
                _manifest = new PackManifest
                {
                    PackVersion = "vanilla",
                    GameVersion = ActiveGameVersion(),
                    Mods = new List<PackModEntry>(),
                    Remove = new List<string>()
                };

                _signatureNote = "Vanilla profile: no pack mods are installed.";
                _switchingPack = !string.Equals(
                    _state.ActivePackId ?? "",
                    _activePack.Id,
                    StringComparison.OrdinalIgnoreCase);

                string vanillaError;
                List<ModChange> vanillaChanges = PackInstaller.ComputeChanges(
                    _manifest,
                    _state,
                    _config,
                    _activePack.Id,
                    out vanillaError);

                if (vanillaChanges == null)
                {
                    HandleCheckFailure(vanillaError);
                    _checking = false;
                    yield break;
                }

                if (vanillaChanges.Count == 0)
                {
                    PersistActivePack(_manifest.PackVersion);
                    _ui = UiState.NoUpdate;
                    _autoHideAt = Time.realtimeSinceStartup + 3f;
                    _checking = false;
                    yield break;
                }

                _changes = vanillaChanges;
                _totalBytes = 0;
                _ui = UiState.UpdateAvailable;
                _checking = false;
                yield break;
            }

            string configError;
            if (!ValidateProfile(_activePack, out configError))
            {
                _ui = UiState.ConfigError;
                _error = configError;
                _checking = false;
                yield break;
            }

            string manifestUrl = EffectiveManifestUrl(_activePack);
            string signatureUrl = EffectiveSignatureUrl(_activePack);
            string accessToken = EffectiveAccessToken(_activePack);

            byte[] manifestBytes = null;
            string fetchError = null;
            yield return FetchBytes(
                PackUrls.WithToken(manifestUrl, accessToken),
                2 * 1024 * 1024,
                bytes => manifestBytes = bytes,
                error => fetchError = error);

            if (fetchError != null)
            {
                HandleCheckFailure("Could not download the manifest: " + fetchError);
                _checking = false;
                yield break;
            }

            if (!string.IsNullOrEmpty(_config.PublicKeyXml) || _config.RequireSignature)
            {
                if (string.IsNullOrEmpty(signatureUrl))
                {
                    HandleCheckFailure("This pack requires a manifest signature, but no SignatureUrl is configured.");
                    _checking = false;
                    yield break;
                }

                byte[] signatureBytes = null;
                string signatureError = null;
                yield return FetchBytes(
                    PackUrls.WithToken(signatureUrl, accessToken),
                    256 * 1024,
                    bytes => signatureBytes = bytes,
                    error => signatureError = error);

                if (signatureError != null)
                {
                    if (_config.RequireSignature || !string.IsNullOrEmpty(_config.PublicKeyXml))
                    {
                        HandleCheckFailure("Could not download the manifest signature: " + signatureError);
                        _checking = false;
                        yield break;
                    }

                    _unsigned = true;
                }
                else
                {
                    try
                    {
                        string base64 = Encoding.UTF8.GetString(signatureBytes).Trim().TrimStart('\uFEFF');
                        signatureBytes = Convert.FromBase64String(base64);
                    }
                    catch (Exception exception)
                    {
                        HandleCheckFailure("The manifest signature is not valid base64: " + exception.Message);
                        _checking = false;
                        yield break;
                    }

                    string verifyError;
                    if (!PackSignature.Verify(manifestBytes, signatureBytes, _config.PublicKeyXml, out verifyError))
                    {
                        HandleCheckFailure(verifyError);
                        _checking = false;
                        yield break;
                    }

                    _signatureNote = "Manifest signature verified.";
                }
            }
            else
            {
                _unsigned = true;
            }

            PackManifest manifest = null;
            try
            {
                string json = Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF');
                manifest = JsonConvert.DeserializeObject<PackManifest>(json);
            }
            catch (Exception exception)
            {
                HandleCheckFailure("Could not parse the manifest: " + exception.Message);
                _checking = false;
                yield break;
            }

            string manifestError;
            if (!PackInstaller.ValidateManifest(manifest, _config, out manifestError))
            {
                HandleCheckFailure(manifestError);
                _checking = false;
                yield break;
            }

            _manifest = manifest;

            string changeError;
            List<ModChange> changes = PackInstaller.ComputeChanges(
                manifest,
                _state,
                _config,
                _activePack.Id,
                out changeError);

            if (changes == null)
            {
                HandleCheckFailure(changeError);
                _checking = false;
                yield break;
            }

            _switchingPack = !string.IsNullOrEmpty(_state.ActivePackId) &&
                             !string.Equals(
                                 _state.ActivePackId,
                                 _activePack.Id,
                                 StringComparison.OrdinalIgnoreCase);

            if (changes.Count == 0)
            {
                PersistActivePack(manifest.PackVersion);
                _ui = UiState.NoUpdate;
                _autoHideAt = Time.realtimeSinceStartup + 3f;
                _checking = false;
                yield break;
            }

            _changes = changes;
            _totalBytes = changes
                .Where(change => change.Kind != ModChangeKind.Remove)
                .Sum(change => Math.Max(0L, change.Mod.Size));

            _ui = UiState.UpdateAvailable;
            _checking = false;
        }

        private void PersistActivePack(string packVersion)
        {
            if (_state == null || _activePack == null)
            {
                return;
            }

            if (string.Equals(_state.ActivePackId, _activePack.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _state.ActivePackId = _activePack.Id;
            _state.PackVersion = packVersion ?? "";

            try
            {
                _state.Save(PackPaths.StateFile);
                PackLogger.Info("Active pack set to " + _activePack.Id + " (no mod changes).");
            }
            catch (Exception exception)
            {
                PackLogger.Warn("Could not save the active pack: " + exception.Message);
            }
        }

        private string ActiveGameVersion()
        {
            if (_activePack != null && !string.IsNullOrEmpty(_activePack.GameVersion))
            {
                return _activePack.GameVersion;
            }

            return _config != null ? _config.GameVersion : "";
        }

        private string EffectiveManifestUrl(PackProfile profile)
        {
            return profile != null ? profile.ManifestUrl : "";
        }

        private string EffectiveAccessToken(PackProfile profile)
        {
            if (profile != null && !string.IsNullOrEmpty(profile.AccessToken))
            {
                return profile.AccessToken;
            }

            return _config != null ? _config.AccessToken : "";
        }

        private string EffectiveSignatureUrl(PackProfile profile)
        {
            if (profile == null || profile.IsVanilla)
            {
                return "";
            }

            if (!string.IsNullOrEmpty(profile.SignatureUrl))
            {
                return profile.SignatureUrl;
            }

            if (_config != null &&
                !string.IsNullOrEmpty(_config.SignatureUrl) &&
                (string.Equals(profile.Id, _config.DefaultPackId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(profile.Id, "default", StringComparison.OrdinalIgnoreCase)))
            {
                return _config.SignatureUrl;
            }

            Uri uri;
            if (Uri.TryCreate(profile.ManifestUrl, UriKind.Absolute, out uri) &&
                uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                uri.Host.IndexOf("drive.google.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                uri.Host.IndexOf("dropbox.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                uri.Host.IndexOf("1drv.ms", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return profile.ManifestUrl + ".sig";
            }

            return "";
        }

        private IEnumerator FetchBytes(string url, int maxBytes, Action<byte[]> onSuccess, Action<string> onError)
        {
            string currentUrl = PackDownloadResolver.Normalize(url);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                byte[] data = null;
                string error = null;

                using (UnityWebRequest request = UnityWebRequest.Get(currentUrl))
                {
                    request.timeout = 60;
                    request.redirectLimit = 10;
                    request.SetRequestHeader("User-Agent", "QLNPackBootstrap/0.1");

                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        error = request.error + " (HTTP " + request.responseCode + ")";
                    }
                    else
                    {
                        data = request.downloadHandler == null ? null : request.downloadHandler.data;
                    }
                }

                if (error != null)
                {
                    onError(error);
                    yield break;
                }

                if (data == null || data.Length == 0)
                {
                    onError("The server returned an empty response.");
                    yield break;
                }

                if (maxBytes > 0 && data.Length > maxBytes)
                {
                    onError("The response is larger than the configured limit.");
                    yield break;
                }

                if (PackDownloadResolver.IsHtml(data))
                {
                    if (attempt == 0)
                    {
                        string nextUrl;
                        if (PackDownloadResolver.TryResolveGoogleDriveHtml(
                                Encoding.UTF8.GetString(data),
                                currentUrl,
                                out nextUrl))
                        {
                            currentUrl = nextUrl;
                            continue;
                        }
                    }

                    onError(
                        "The URL returned an HTML page instead of a file. " +
                        "Use a direct download link, or make the shared file public.");
                    yield break;
                }

                onSuccess(data);
                yield break;
            }
        }

        private bool ValidateProfile(PackProfile profile, out string error)
        {
            error = null;

            if (_config == null)
            {
                error = "Configuration was not loaded.";
                return false;
            }

            if (profile == null)
            {
                error = "No mod pack profile is selected.";
                return false;
            }

            if (!profile.IsVanilla)
            {
                string manifestUrl = EffectiveManifestUrl(profile);
                if (string.IsNullOrWhiteSpace(manifestUrl))
                {
                    error = "The pack '" + profile.Label +
                            "' has no ManifestUrl. Edit " + PackPaths.ConfigFile + ".";
                    return false;
                }

                Uri uri;
                if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out uri))
                {
                    error = "The pack '" + profile.Label + "' has an invalid ManifestUrl.";
                    return false;
                }

                if (uri.Scheme != Uri.UriSchemeHttps &&
                    !(uri.Scheme == Uri.UriSchemeHttp && _config.AllowInsecureHttp))
                {
                    error = "The pack '" + profile.Label +
                            "' must use HTTPS (or enable AllowInsecureHttp for local testing).";
                    return false;
                }

                if (_config.RequireSignature && string.IsNullOrWhiteSpace(_config.PublicKeyXml))
                {
                    error = "RequireSignature is true, but PublicKeyXml is empty.";
                    return false;
                }

                string signatureUrl = EffectiveSignatureUrl(profile);
                if (_config.RequireSignature && string.IsNullOrWhiteSpace(signatureUrl))
                {
                    error = "RequireSignature is true, but the pack '" + profile.Label +
                            "' has no SignatureUrl.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(_config.PublicKeyXml) &&
                    string.IsNullOrWhiteSpace(signatureUrl))
                {
                    error = "PublicKeyXml is set, but the pack '" + profile.Label +
                            "' has no SignatureUrl. Set the .sig URL explicitly, especially for Drive/Dropbox manifests.";
                    return false;
                }
            }

            string modsRootError;
            if (!ValidateModsRoot(out modsRootError))
            {
                error = modsRootError;
                return false;
            }

            return true;
        }

        private bool ValidateModsRoot(out string error)
        {
            error = null;
            string modsRoot = PackPaths.ModsRoot;

            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                error = "Mods folder not found: " + modsRoot +
                        ". Install the bootstrap under ...\\Mods\\000_QLNPackBootstrap, " +
                        "or set ModsRoot in packbootstrap.xml.";
                return false;
            }

            bool hasOverride = _config != null && !string.IsNullOrWhiteSpace(_config.ModsRoot);

            if (!hasOverride &&
                !string.Equals(Path.GetFileName(modsRoot), "Mods", StringComparison.OrdinalIgnoreCase))
            {
                error = "The bootstrap is running from '" + modsRoot +
                        "', which is not a folder named 'Mods'. Install it under " +
                        "...\\Mods\\000_QLNPackBootstrap, or set ModsRoot in packbootstrap.xml.";
                return false;
            }

            return true;
        }

        private void HandleCheckFailure(string message)
        {
            PackLogger.Error("Pack check failed: " + message);
            _error = message;

            if (_config != null && _config.ShowCheckErrors)
            {
                _ui = UiState.Error;
            }
            else
            {
                _ui = UiState.Hidden;
            }
        }

        // ------------------------------------------------------------------
        // Install
        // ------------------------------------------------------------------

        private IEnumerator InstallRoutine()
        {
            if (_manifest == null || _changes == null)
            {
                yield break;
            }

            string configError;
            if (!ValidateProfile(_activePack, out configError))
            {
                _ui = UiState.ConfigError;
                _error = configError;
                yield break;
            }

            _cancelRequested = false;
            _error = null;
            _progress = 0;
            _ui = UiState.Downloading;

            try
            {
                PackInstaller.CleanupTemp();
                _stageRoot = Path.Combine(PackPaths.StagingDir, _manifest.PackVersion);
                PackInstaller.SafeDeleteDirectory(_stageRoot);
                Directory.CreateDirectory(_stageRoot);
                Directory.CreateDirectory(PackPaths.TempDir);
            }
            catch (Exception exception)
            {
                FailInstall("Could not prepare the staging folder: " + exception.Message);
                yield break;
            }

            List<ModChange> downloads = _changes
                .Where(change => change.Kind != ModChangeKind.Remove)
                .ToList();

            _totalBytes = downloads.Sum(change => Math.Max(0L, change.Mod.Size));
            _maxParallelDownloads = Mathf.Clamp(_config.MaxParallelDownloads, 1, 10);
            _jobs = new List<DownloadJob>();
            _completedJobs = 0;

            foreach (ModChange change in downloads)
            {
                _jobs.Add(new DownloadJob
                {
                    Change = change,
                    Url = PackUrls.WithToken(
                        PackDownloadResolver.Normalize(change.Mod.Url),
                        _config.AccessToken),
                    ZipPath = Path.Combine(PackPaths.TempDir, change.Folder + ".zip")
                });
            }

            int nextJob = 0;

            while (nextJob < _jobs.Count)
            {
                if (_cancelRequested)
                {
                    CancelCleanup();
                    yield break;
                }

                List<DownloadJob> batch = new List<DownloadJob>();
                while (batch.Count < _maxParallelDownloads && nextJob < _jobs.Count)
                {
                    batch.Add(_jobs[nextJob]);
                    nextJob++;
                }

                foreach (DownloadJob job in batch)
                {
                    StartDownload(job);
                }

                while (batch.Exists(job => job.State == DownloadState.Downloading))
                {
                    if (_cancelRequested)
                    {
                        AbortAllRequests();
                        CancelCleanup();
                        yield break;
                    }

                    foreach (DownloadJob job in batch)
                    {
                        if (job.Request == null)
                        {
                            continue;
                        }

                        job.Progress = job.Request.downloadProgress;

                        if (job.Operation != null && job.Operation.isDone)
                        {
                            CompleteDownload(job);
                        }
                    }

                    if (batch.Exists(job => job.State == DownloadState.Failed))
                    {
                        DownloadJob failed = batch.First(job => job.State == DownloadState.Failed);
                        AbortAllRequests();
                        FailInstall(failed.Error ?? ("Download failed: " + failed.Label));
                        yield break;
                    }

                    yield return null;
                }

                foreach (DownloadJob job in batch)
                {
                    if (job.State != DownloadState.Downloaded)
                    {
                        continue;
                    }

                    if (_cancelRequested)
                    {
                        CancelCleanup();
                        yield break;
                    }

                    _currentName = job.Label;
                    _ui = UiState.Downloading;
                    job.State = DownloadState.Verifying;

                    Task<string> hashTask = Task.Run(() => PackInstaller.ComputeSha256(job.ZipPath));
                    while (!hashTask.IsCompleted)
                    {
                        yield return null;
                    }

                    if (hashTask.IsFaulted)
                    {
                        FailInstall("Could not hash " + job.Change.Folder + ": " + Unwrap(hashTask.Exception));
                        yield break;
                    }

                    string hash = hashTask.Result;
                    if (!string.Equals(hash, job.Change.Mod.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        FailInstall(
                            "SHA-256 mismatch for " + job.Change.Folder +
                            ". Expected " + job.Change.Mod.Sha256 + ", got " + hash + ".");
                        yield break;
                    }

                    job.State = DownloadState.Extracting;

                    Task extractTask = Task.Run(() => StageOne(job.Change, job.ZipPath));
                    while (!extractTask.IsCompleted)
                    {
                        yield return null;
                    }

                    if (extractTask.IsFaulted)
                    {
                        FailInstall("Could not extract " + job.Change.Folder + ": " + Unwrap(extractTask.Exception));
                        yield break;
                    }

                    job.State = DownloadState.Done;
                    job.Progress = 1f;
                    _completedJobs++;

                    try
                    {
                        File.Delete(job.ZipPath);
                    }
                    catch
                    {
                        // Temp cleanup will get it.
                    }

                    yield return null;
                }
            }

            if (_cancelRequested)
            {
                CancelCleanup();
                yield break;
            }

            _ui = UiState.Downloading;

            Task<StageResult> stageTask = Task.Run(StagePendingSafe);
            while (!stageTask.IsCompleted)
            {
                yield return null;
            }

            if (stageTask.IsFaulted)
            {
                FailInstall("Could not prepare the update: " + Unwrap(stageTask.Exception));
                yield break;
            }

            StageResult result = stageTask.Result;
            if (!result.Ok)
            {
                FailInstall(result.Error);
                yield break;
            }

            _pending = result.Pending;
            PackInstaller.CleanupTemp();
            _ui = UiState.Staged;

            if (_config.AutoRestart)
            {
                ScheduleAutoRestart();
            }
        }

        private void StartDownload(DownloadJob job)
        {
            if (job.Request != null)
            {
                try
                {
                    job.Request.Abort();
                    job.Request.Dispose();
                }
                catch
                {
                    // Best effort.
                }

                job.Request = null;
                job.Operation = null;
            }

            try
            {
                if (File.Exists(job.ZipPath))
                {
                    File.Delete(job.ZipPath);
                }

                UnityWebRequest request = UnityWebRequest.Get(job.Url);
                request.timeout = 0;
                request.redirectLimit = 10;
                request.SetRequestHeader("User-Agent", "QLNPackBootstrap/0.1");
                request.downloadHandler = new DownloadHandlerFile(job.ZipPath);

                job.Request = request;
                job.State = DownloadState.Downloading;
                job.Progress = 0f;
                job.Error = null;
                job.Operation = request.SendWebRequest();
            }
            catch (Exception exception)
            {
                job.Error = "Could not start download: " + exception.Message;
                job.State = DownloadState.Failed;
            }
        }

        private void CompleteDownload(DownloadJob job)
        {
            UnityWebRequest request = job.Request;
            job.Request = null;
            job.Operation = null;

            if (request == null)
            {
                return;
            }

            bool retry = false;

            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    job.Error = request.error + " (HTTP " + request.responseCode + ")";
                    job.State = DownloadState.Failed;
                    return;
                }

                string contentType = request.GetResponseHeader("Content-Type") ?? "";
                bool looksHtml =
                    contentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    PackDownloadResolver.IsHtmlFile(job.ZipPath);

                if (looksHtml)
                {
                    if (job.Attempt == 0)
                    {
                        try
                        {
                            string html = File.ReadAllText(job.ZipPath, Encoding.UTF8);
                            string nextUrl;

                            if (PackDownloadResolver.TryResolveGoogleDriveHtml(html, job.Url, out nextUrl))
                            {
                                job.Url = nextUrl;
                                job.Attempt++;
                                job.Progress = 0f;
                                retry = true;
                            }
                        }
                        catch
                        {
                            // Clear error below.
                        }
                    }

                    if (!retry)
                    {
                        job.Error =
                            "The provider returned an HTML page instead of a zip. " +
                            "Use a direct download link, or make the shared file public.";
                        job.State = DownloadState.Failed;
                        return;
                    }
                }
                else
                {
                    long downloadedLength = 0;

                    try
                    {
                        downloadedLength = new FileInfo(job.ZipPath).Length;
                    }
                    catch
                    {
                        // Size check below will fail.
                    }

                    if (job.Change.Mod.Size > 0 && downloadedLength != job.Change.Mod.Size)
                    {
                        job.Error = "Downloaded " + downloadedLength +
                                    " bytes, expected " + job.Change.Mod.Size + ".";
                        job.State = DownloadState.Failed;
                        return;
                    }

                    job.State = DownloadState.Downloaded;
                    job.Progress = 1f;
                }
            }
            finally
            {
                try
                {
                    request.Dispose();
                }
                catch
                {
                    // Best effort.
                }
            }

            if (retry)
            {
                try
                {
                    if (File.Exists(job.ZipPath))
                    {
                        File.Delete(job.ZipPath);
                    }
                }
                catch
                {
                    // StartDownload will overwrite.
                }

                StartDownload(job);
            }
        }

        private void AbortAllRequests()
        {
            if (_jobs == null)
            {
                return;
            }

            foreach (DownloadJob job in _jobs)
            {
                if (job.Request == null)
                {
                    continue;
                }

                try
                {
                    job.Request.Abort();
                    job.Request.Dispose();
                }
                catch
                {
                    // Best effort.
                }

                job.Request = null;
                job.Operation = null;

                if (job.State == DownloadState.Downloading)
                {
                    job.State = DownloadState.Failed;
                }
            }
        }

        private void StageOne(ModChange change, string zipPath)
        {
            // Extract into a short path. Deep mods like SCore have internal
            // paths that would otherwise exceed MAX_PATH under the staging tree.
            string extractRoot = PackPaths.ExtractDir;
            Directory.CreateDirectory(extractRoot);

            string extractDir = Path.Combine(
                extractRoot,
                Guid.NewGuid().ToString("N").Substring(0, 10));

            PackInstaller.SafeDeleteDirectory(extractDir);
            Directory.CreateDirectory(extractDir);

            PackInstaller.ExtractZipSafe(
                zipPath,
                extractDir,
                _config.MaxEntries,
                _config.MaxUncompressedBytes);

            string modRoot = PackInstaller.FindModRoot(extractDir);
            if (modRoot == null)
            {
                throw new InvalidDataException(
                    "The archive contains more than one top-level ModInfo.xml. Split it into separate mods.");
            }

            string destination = Path.Combine(_stageRoot, change.Folder);
            PackInstaller.SafeDeleteDirectory(destination);

            if (string.Equals(
                    Path.GetFullPath(modRoot),
                    Path.GetFullPath(extractDir),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(extractDir, destination);
            }
            else
            {
                Directory.Move(modRoot, destination);
                PackInstaller.SafeDeleteDirectory(extractDir);
            }

            if (!Directory.EnumerateFileSystemEntries(destination).Any())
            {
                throw new InvalidDataException("The archive is empty.");
            }
        }

        private StageResult StagePendingSafe()
        {
            PackPending pending;
            string error;

            bool ok = PackInstaller.StagePending(
                _manifest,
                _changes,
                _stageRoot,
                _config,
                _activePack != null ? _activePack.Id : "",
                out pending,
                out error);

            return new StageResult
            {
                Ok = ok,
                Pending = pending,
                Error = error
            };
        }

        private void FailInstall(string message)
        {
            _ui = UiState.Error;
            _error = message;
            PackLogger.Error("Install failed: " + message);

            AbortAllRequests();

            if (_stageRoot != null)
            {
                PackInstaller.SafeDeleteDirectory(_stageRoot);
            }

            PackInstaller.CleanupTemp();
        }

        private void CancelInstall()
        {
            _cancelRequested = true;
            AbortAllRequests();
        }

        private void CancelCleanup()
        {
            _cancelRequested = false;

            if (_stageRoot != null)
            {
                PackInstaller.SafeDeleteDirectory(_stageRoot);
            }

            PackInstaller.CleanupTemp();
            _ui = _changes != null ? UiState.UpdateAvailable : UiState.Hidden;
        }

        private static string Unwrap(Exception exception)
        {
            if (exception == null)
            {
                return "unknown error";
            }

            AggregateException aggregate = exception as AggregateException;
            if (aggregate != null && aggregate.InnerException != null)
            {
                return Unwrap(aggregate.InnerException);
            }

            return exception.Message;
        }

        // ------------------------------------------------------------------
        // UI
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (_config == null || GameManager.IsDedicatedServer)
            {
                return;
            }

            if (GameManager.Instance != null && GameManager.Instance.World != null)
            {
                return;
            }

            EnsureStyles();

            _uiScale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.5f);
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(
                Vector3.zero,
                Quaternion.identity,
                new Vector3(_uiScale, _uiScale, 1f));

            float screenWidth = Screen.width / _uiScale;
            float screenHeight = Screen.height / _uiScale;

            if (_ui == UiState.Hidden)
            {
                DrawMainMenuButton(screenWidth, screenHeight);
                GUI.matrix = oldMatrix;
                return;
            }

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, screenWidth, screenHeight), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float width = Mathf.Min(940f, screenWidth - 40f);
            float height = Mathf.Min(760f, screenHeight - 40f);
            Rect rect = new Rect(
                (screenWidth - width) / 2f,
                (screenHeight - height) / 2f,
                width,
                height);

            GUI.Window(0x514C4E, rect, DrawWindow, "QLN", _windowStyle);
            GUI.matrix = oldMatrix;
        }

        private void DrawMainMenuButton(float screenWidth, float screenHeight)
        {
            if (!_mainMenuOpen ||
                _config == null ||
                !_config.ShowMainMenuButton)
            {
                return;
            }

            bool staged = File.Exists(PackPaths.PendingFile);
            string label = staged ? "Finish Update" : "Mod Packs";
            Rect button = new Rect(screenWidth - 220f, 16f, 204f, 38f);

            if (GUI.Button(button, label, _menuButton))
            {
                BeginCheck(true);
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            Color text = new Color(0.92f, 0.87f, 0.76f);
            Color smallText = new Color(0.74f, 0.67f, 0.55f);
            Color accent = new Color(0.86f, 0.57f, 0.23f);

            _panelTex = MakeBoxTexture(
                new Color(0.10f, 0.08f, 0.06f, 0.98f),
                new Color(0.36f, 0.25f, 0.13f, 1f));
            _buttonTex = MakeBoxTexture(
                new Color(0.17f, 0.13f, 0.09f, 1f),
                new Color(0.38f, 0.27f, 0.14f, 1f));
            _buttonHoverTex = MakeBoxTexture(
                new Color(0.25f, 0.18f, 0.11f, 1f),
                new Color(0.62f, 0.40f, 0.18f, 1f));
            _buttonActiveTex = MakeBoxTexture(
                new Color(0.33f, 0.23f, 0.13f, 1f),
                accent);
            _accentTex = MakeBoxTexture(accent, accent);

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(12, 12, 10, 12)
            };
            _windowStyle.normal.background = _panelTex;
            _windowStyle.normal.textColor = text;
            _windowStyle.onNormal.background = _panelTex;
            _windowStyle.onNormal.textColor = text;

            _wrap = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true
            };
            _wrap.normal.textColor = text;

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold
            };
            _title.normal.textColor = text;

            _small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };
            _small.normal.textColor = smallText;

            _button = CreateButtonStyle(
                _buttonTex,
                _buttonHoverTex,
                _buttonActiveTex,
                text,
                text,
                13);

            _tab = CreateButtonStyle(
                _buttonTex,
                _buttonHoverTex,
                _buttonActiveTex,
                smallText,
                text,
                12);

            _tabActive = CreateButtonStyle(
                _buttonActiveTex,
                _buttonHoverTex,
                _buttonActiveTex,
                text,
                text,
                12);
            _tabActive.fontStyle = FontStyle.Bold;

            _menuButton = CreateButtonStyle(
                _buttonTex,
                _buttonHoverTex,
                _buttonActiveTex,
                text,
                text,
                13);
            _menuButton.fontStyle = FontStyle.Bold;

            _stylesReady = true;
        }

        private static GUIStyle CreateButtonStyle(
            Texture2D normal,
            Texture2D hover,
            Texture2D active,
            Color normalText,
            Color activeText,
            int fontSize)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(10, 10, 6, 6)
            };

            style.normal.background = normal;
            style.normal.textColor = normalText;
            style.hover.background = hover;
            style.hover.textColor = activeText;
            style.active.background = active;
            style.active.textColor = activeText;
            style.focused.background = hover;
            style.focused.textColor = activeText;
            style.onNormal.background = active;
            style.onNormal.textColor = activeText;
            style.onHover.background = active;
            style.onHover.textColor = activeText;
            return style;
        }

        private static Texture2D MakeBoxTexture(Color fill, Color border)
        {
            Texture2D texture = new Texture2D(3, 3, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    bool edge = x == 0 || y == 0 || x == 2 || y == 2;
                    texture.SetPixel(x, y, edge ? border : fill);
                }
            }

            texture.Apply();
            return texture;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.Label("QLN Mod Packs", _title);

            Rect accentLine = GUILayoutUtility.GetRect(1f, 2f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(accentLine, _accentTex);
            GUILayout.Space(2f);

            DrawPackTabs();

            string packVersion = ActivePackIsVanilla()
                ? "Vanilla"
                : (_manifest != null
                    ? _manifest.PackVersion
                    : (_pending != null ? _pending.PackVersion : "unknown"));

            string packLabel = _activePack != null ? _activePack.Label : "none";
            string source = _activePack != null && !_activePack.IsVanilla
                ? "    Source: " + HostOf(EffectiveManifestUrl(_activePack))
                : "";

            GUILayout.Label(
                "Pack: " + packLabel +
                "    Version: " + packVersion +
                "    Game: " + ActiveGameVersion() +
                source,
                _small);

            if (_unsigned)
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.72f, 0.3f);
                GUILayout.Label(
                    "WARNING: This manifest is not signed. Only install it if you trust the server owner.",
                    _wrap);
                GUI.color = oldColor;
            }

            if (!string.IsNullOrEmpty(_signatureNote))
            {
                GUILayout.Label(_signatureNote, _small);
            }

            if (GameVersionMismatch())
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.3f);
                GUILayout.Label(
                    "Warning: the manifest targets game " + _manifest.GameVersion +
                    ", but this bootstrap is configured for " + ActiveGameVersion() + ".",
                    _wrap);
                GUI.color = oldColor;
            }

            GUILayout.Space(6f);

            switch (_ui)
            {
                case UiState.Checking:
                    GUILayout.Label("Checking for mod pack updates...", _wrap);
                    GUILayout.FlexibleSpace();
                    break;

                case UiState.NoUpdate:
                    GUILayout.Label("Your mod pack is up to date.", _wrap);
                    GUILayout.FlexibleSpace();
                    DrawButtons(
                        ("Recheck", () => BeginCheck(true)),
                        ("Close", () => { _dismissed = true; _ui = UiState.Hidden; }));
                    break;

                case UiState.UpdateAvailable:
                    DrawUpdateAvailable();
                    break;

                case UiState.Downloading:
                    DrawDownloads();
                    break;

                case UiState.Verifying:
                    GUILayout.Label("Verifying " + _currentName + "...", _wrap);
                    GUILayout.Label("Checking the downloaded file against its SHA-256 hash.", _small);
                    GUILayout.FlexibleSpace();
                    DrawButtons(("Cancel", CancelInstall));
                    break;

                case UiState.Extracting:
                    GUILayout.Label("Installing " + _currentName + "...", _wrap);
                    GUILayout.Label("Extracting the mod and preparing the update.", _small);
                    GUILayout.FlexibleSpace();
                    DrawButtons(("Cancel", CancelInstall));
                    break;

                case UiState.Staged:
                    DrawStaged();
                    break;

                case UiState.Error:
                    GUILayout.Label("Error", _title);
                    GUILayout.Label(_error ?? "Unknown error.", _wrap);
                    GUILayout.FlexibleSpace();
                    DrawButtons(
                        ("Retry", Retry),
                        ("Open Log", () => OpenPath(PackPaths.LogsDir)),
                        ("Close", () => { _dismissed = true; _ui = UiState.Hidden; }));
                    break;

                case UiState.ConfigError:
                    GUILayout.Label("Configuration needed", _title);
                    GUILayout.Label(_error ?? "The bootstrap configuration is incomplete.", _wrap);
                    GUILayout.Label("Config file: " + PackPaths.ConfigFile, _small);
                    GUILayout.FlexibleSpace();
                    DrawButtons(
                        ("Open Config", OpenConfig),
                        ("Open Log", () => OpenPath(PackPaths.LogsDir)),
                        ("Recheck", () => BeginCheck(true)),
                        ("Close", () => { _dismissed = true; _ui = UiState.Hidden; }));
                    break;
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawPackTabs()
        {
            if (_packs == null || _packs.Count == 0)
            {
                return;
            }

            bool oldEnabled = GUI.enabled;
            bool busy = _checking ||
                        _ui == UiState.Checking ||
                        _ui == UiState.Downloading ||
                        _ui == UiState.Verifying ||
                        _ui == UiState.Extracting ||
                        _ui == UiState.Staged ||
                        File.Exists(PackPaths.PendingFile);
            GUI.enabled = !busy;

            _packScroll = GUILayout.BeginScrollView(
                _packScroll,
                false,
                false,
                GUILayout.Height(38f));

            GUILayout.BeginHorizontal();

            foreach (PackProfile pack in _packs)
            {
                bool active = _activePack != null &&
                              string.Equals(_activePack.Id, pack.Id, StringComparison.OrdinalIgnoreCase);

                if (GUILayout.Button(pack.Label, active ? _tabActive : _tab, GUILayout.Height(30f)))
                {
                    if (!active)
                    {
                        _activePack = pack;
                        _dismissed = false;
                        _ui = UiState.Checking;
                        BeginCheck(true);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_config.PackIndexUrl))
            {
                if (GUILayout.Button("Refresh Packs", _tab, GUILayout.Height(30f)))
                {
                    RefreshPackList();
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            GUI.enabled = oldEnabled;
            GUILayout.Space(4f);
        }

        private bool ActivePackIsVanilla()
        {
            return _activePack != null && _activePack.IsVanilla;
        }

        private void DrawUpdateAvailable()
        {
            bool vanilla = ActivePackIsVanilla();

            if (vanilla)
            {
                GUILayout.Label(
                    "Vanilla profile selected. All mod pack folders managed by the bootstrap will be removed.",
                    _wrap);
            }
            else
            {
                GUILayout.Label(
                    "The selected mod pack has an update (" + PackInstaller.SizeText(_totalBytes) + " to download).",
                    _wrap);

                GUILayout.Label(
                    "Pressing Download & Install downloads the files below and installs them into:\n" +
                    PackPaths.ModsRoot +
                    "\n\nExisting versions are backed up first. The game must be restarted to apply the update.",
                    _wrap);
            }

            if (_switchingPack)
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.3f);
                GUILayout.Label(
                    "Switching packs: folders from the previous pack that are not part of this pack will be removed.",
                    _wrap);
                GUI.color = oldColor;
            }

            DrawChanges();
            GUILayout.FlexibleSpace();

            DrawButtons(
                (vanilla ? "Switch to Vanilla" : "Download & Install", () => StartCoroutine(InstallRoutine())),
                ("Later", () =>
                {
                    _dismissed = true;
                    _ui = UiState.Hidden;
                }),
                ("Recheck", () => BeginCheck(true)));
        }

        private void DrawStaged()
        {
            string version = _pending != null ? _pending.PackVersion : "current";

            if (_autoRestartScheduled)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(_autoRestartAt - Time.realtimeSinceStartup));
                GUILayout.Label("All mods downloaded", _title);
                GUILayout.Label(
                    "Pack " + version + " is staged. The client will restart in " + seconds +
                    " second(s), apply the files, and start again.",
                    _wrap);
            }
            else
            {
                GUILayout.Label("Restart required", _title);
                GUILayout.Label(
                    "The update for pack " + version + " is staged. Restart & Update closes the game, " +
                    "applies the files, and starts the game again.",
                    _wrap);
            }

            if (File.Exists(PackPaths.UpdaterFailedFile))
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.72f, 0.3f);
                GUILayout.Label(
                    "The last automatic apply failed. Check the updater log, then click Restart & Update to retry.",
                    _wrap);
                GUI.color = oldColor;
            }

            if (_pending != null && _pending.Apply.Count > 0)
            {
                GUILayout.Label("The following mod folders will be replaced at the next launch:", _small);
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(140f));
                foreach (PendingMod mod in _pending.Apply)
                {
                    GUILayout.Label("- " + (string.IsNullOrEmpty(mod.DisplayName) ? mod.Folder : mod.DisplayName) +
                                    " v" + mod.Version, _wrap);
                }

                GUILayout.EndScrollView();
            }
            else if (_pending != null && _pending.Remove.Count > 0)
            {
                GUILayout.Label(
                    "No mod folders need replacing; obsolete pack folders will be backed up.",
                    _wrap);
            }

            GUILayout.Label("Log: " + PackPaths.LogFile, _small);
            GUILayout.FlexibleSpace();

            if (_autoRestartScheduled)
            {
                DrawButtons(
                    ("Restart Now", StartUpdaterAndQuit),
                    ("Cancel Auto-Restart", () =>
                    {
                        _autoRestartScheduled = false;
                        _ui = UiState.Staged;
                    }),
                    ("Open Log", () => OpenPath(PackPaths.LogsDir)));
            }
            else
            {
                DrawButtons(
                    ("Restart & Update", StartUpdaterAndQuit),
                    ("Later", () => { _dismissed = true; _ui = UiState.Hidden; }),
                    ("Open Log", () => OpenPath(PackPaths.LogsDir)));
            }
        }

        private void DrawChanges()
        {
            if (_changes == null || _changes.Count == 0)
            {
                return;
            }

            float listHeight = Mathf.Max(150f, (Screen.height / _uiScale) * 0.38f);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(listHeight));

            foreach (ModChange change in _changes)
            {
                if (change.Kind == ModChangeKind.Remove)
                {
                    GUILayout.Label("- Remove " + change.Folder + " (backed up)", _wrap);
                    continue;
                }

                string status;
                switch (change.Kind)
                {
                    case ModChangeKind.New:
                        status = "new";
                        break;
                    case ModChangeKind.UnmanagedExisting:
                        status = "existing folder; will be backed up and replaced";
                        break;
                    default:
                        string oldVersion = InstalledVersion(change.Folder);
                        status = string.IsNullOrEmpty(oldVersion)
                            ? "update"
                            : "update " + oldVersion + " -> " + change.Mod.Version;
                        break;
                }

                GUILayout.Label(
                    "- " + change.Mod.Label +
                    "  v" + change.Mod.Version +
                    "  (" + PackInstaller.SizeText(change.Mod.Size) + ")  - " + status,
                    _wrap);
            }

            GUILayout.EndScrollView();
        }

        private void DrawDownloads()
        {
            int total = _jobs != null ? _jobs.Count : 0;
            int done = _jobs != null ? _jobs.Count(job => job.State == DownloadState.Done) : 0;

            GUILayout.Label(
                "Downloading mods: " + done + " / " + total + " complete" +
                "    Parallel: " + _maxParallelDownloads,
                _wrap);

            if (_jobs != null && _jobs.Count > 0)
            {
                float listHeight = Mathf.Max(170f, (Screen.height / _uiScale) * 0.42f);

                _downloadScroll = GUILayout.BeginScrollView(
                    _downloadScroll,
                    GUILayout.Height(listHeight));

                foreach (DownloadJob job in _jobs)
                {
                    GUILayout.Label(job.Label + "  -  " + JobStatus(job), _wrap);
                    DrawBar(job.State == DownloadState.Done ? 1f : Mathf.Clamp01(job.Progress));
                    GUILayout.Space(4f);
                }

                GUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();
            DrawButtons(("Cancel", CancelInstall));
        }

        private static string JobStatus(DownloadJob job)
        {
            switch (job.State)
            {
                case DownloadState.Pending:
                    return "waiting";
                case DownloadState.Downloading:
                    return "downloading " + (job.Progress * 100f).ToString("0") + "%";
                case DownloadState.Downloaded:
                    return "downloaded";
                case DownloadState.Verifying:
                    return "verifying SHA-256";
                case DownloadState.Extracting:
                    return "extracting";
                case DownloadState.Done:
                    return "done";
                case DownloadState.Failed:
                    return "failed" + (string.IsNullOrEmpty(job.Error) ? "" : ": " + job.Error);
                default:
                    return "";
            }
        }

        private void DrawBar(float value)
        {
            Rect bar = GUILayoutUtility.GetRect(10f, 14f, GUILayout.ExpandWidth(true));
            GUI.Box(bar, GUIContent.none);

            Color oldColor = GUI.color;
            GUI.color = new Color(0.2f, 0.75f, 0.3f);
            GUI.DrawTexture(
                new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * Mathf.Clamp01(value), bar.height - 4f),
                Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        private void DrawProgress()
        {
            DrawBar(_progress);
            GUILayout.Label((_progress * 100f).ToString("0") + "%", _small);
        }

        private void DrawButtons(params (string Label, Action Action)[] buttons)
        {
            GUILayout.BeginHorizontal();

            foreach ((string label, Action action) in buttons)
            {
                if (GUILayout.Button(label, _button, GUILayout.Height(32f)))
                {
                    action();
                }
            }

            GUILayout.EndHorizontal();
        }

        private void Retry()
        {
            _error = null;

            if (_changes != null)
            {
                _ui = UiState.UpdateAvailable;
            }
            else
            {
                BeginCheck(true);
            }
        }

        private string InstalledVersion(string folder)
        {
            if (_state == null)
            {
                return "";
            }

            InstalledMod installed = _state.Mods.FirstOrDefault(
                mod => mod != null && string.Equals(mod.Folder, folder, StringComparison.OrdinalIgnoreCase));

            return installed == null ? "" : installed.Version;
        }

        private bool GameVersionMismatch()
        {
            string activeVersion = ActiveGameVersion();

            if (_manifest == null ||
                string.IsNullOrEmpty(_manifest.GameVersion) ||
                string.IsNullOrEmpty(activeVersion))
            {
                return false;
            }

            return !_manifest.GameVersion.StartsWith(activeVersion, StringComparison.OrdinalIgnoreCase);
        }

        private static string HostOf(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri.Host : "(invalid)";
        }

        private void ScheduleAutoRestart()
        {
            _autoRestartAt = Time.realtimeSinceStartup + 5f;
            _autoRestartScheduled = true;
        }

        private void StartUpdaterAndQuit()
        {
            _autoRestartScheduled = false;

            try
            {
                string updater = Path.Combine(PackPaths.BootstrapDir, "QLNPackUpdater.exe");
                if (!File.Exists(updater))
                {
                    _ui = UiState.Error;
                    _error = "Updater not found: " + updater + ". Reinstall the bootstrap mod.";
                    return;
                }

                string tempDir = Path.Combine(
                    Path.GetTempPath(),
                    "QLNPackUpdater",
                    Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(tempDir);
                string tempExe = Path.Combine(tempDir, "QLNPackUpdater.exe");
                File.Copy(updater, tempExe, true);

                string gameExe = ResolveGameExe();
                StringBuilder arguments = new StringBuilder();
                arguments.Append("--pid ").Append(Process.GetCurrentProcess().Id);
                arguments.Append(" --pending \"").Append(PackPaths.PendingFile).Append('"');
                arguments.Append(" --mods \"").Append(PackPaths.ModsRoot).Append('"');
                arguments.Append(" --data \"").Append(PackPaths.DataDir).Append('"');
                arguments.Append(" --keep ").Append(Math.Max(1, _config.KeepBackups));

                if (!string.IsNullOrEmpty(gameExe) && _config.AutoRelaunch)
                {
                    arguments.Append(" --game \"").Append(gameExe).Append('"');
                    arguments.Append(" --relaunch");
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = tempExe,
                    Arguments = arguments.ToString(),
                    WorkingDirectory = tempDir,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                Process.Start(startInfo);
                PackLogger.Info("Started updater: " + tempExe + " " + arguments);
                Application.Quit();
            }
            catch (Exception exception)
            {
                _ui = UiState.Error;
                _error = "Could not start the updater: " + exception.Message;
                PackLogger.Exception("Could not start the updater", exception);
            }
        }

        private string ResolveGameExe()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_config.GameExe) && File.Exists(_config.GameExe))
                {
                    return _config.GameExe;
                }

                string gameRoot = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(gameRoot))
                {
                    return "";
                }

                string exe = Path.Combine(gameRoot, "7DaysToDie.exe");
                return File.Exists(exe) ? exe : "";
            }
            catch
            {
                return "";
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                PackLogger.Warn("Could not open '" + path + "': " + exception.Message);
            }
        }

        private static void OpenConfig()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = "\"" + PackPaths.ConfigFile + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                PackLogger.Warn("Could not open the config file: " + exception.Message);
            }
        }

        private enum DownloadState
        {
            Pending,
            Downloading,
            Downloaded,
            Verifying,
            Extracting,
            Done,
            Failed
        }

        private sealed class DownloadJob
        {
            public ModChange Change;
            public string Url;
            public string ZipPath;
            public UnityWebRequest Request;
            public UnityWebRequestAsyncOperation Operation;
            public DownloadState State = DownloadState.Pending;
            public float Progress;
            public int Attempt;
            public string Error;

            public string Label
            {
                get { return Change != null && Change.Mod != null ? Change.Mod.Label : ""; }
            }
        }

        private sealed class StageResult
        {
            public bool Ok;
            public PackPending Pending;
            public string Error;
        }
    }
}
