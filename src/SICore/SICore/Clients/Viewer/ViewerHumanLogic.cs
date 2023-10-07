﻿using Notions;
using SICore.BusinessLogic;
using SICore.Clients.Viewer;
using SIData;
using SIPackages.Core;
using SIUI.ViewModel;
using SIUI.ViewModel.Core;
using System.Diagnostics;
using System.Text;
using System.Xml;
using Utils;
using R = SICore.Properties.Resources;

namespace SICore;

/// <summary>
/// Логика зрителя-человека
/// </summary>
public class ViewerHumanLogic : Logic<ViewerData>, IViewerLogic
{
    private static readonly TimeSpan HintLifetime = TimeSpan.FromSeconds(6);

    private bool _disposed = false;

    private readonly CancellationTokenSource _cancellation = new();

    private readonly ILocalFileManager _localFileManager = new LocalFileManager();
    private readonly Task _localFileManagerTask;

    protected readonly ViewerActions _viewerActions;

    protected readonly ILocalizer _localizer;

    public TableInfoViewModel TInfo { get; }

    public bool CanSwitchType => true;

    public IPlayerLogic PlayerLogic { get; }

    public IShowmanLogic ShowmanLogic { get; }

    public ViewerHumanLogic(ViewerData data, ViewerActions viewerActions, ILocalizer localizer)
        : base(data)
    {
        _viewerActions = viewerActions;
        _localizer = localizer;

        TInfo = new TableInfoViewModel(_data.TInfo, _data.BackLink.GetSettings()) { AnimateText = true, Enabled = true };

        TInfo.PropertyChanged += TInfo_PropertyChanged;
        TInfo.MediaLoad += TInfo_MediaLoad;
        TInfo.MediaLoadError += TInfo_MediaLoadError;

        //PlayerLogic = new PlayerHumanLogic(data, TInfo, viewerActions, localizer);
        //ShowmanLogic = new ShowmanHumanLogic(data, TInfo, viewerActions, localizer);

        _localFileManager.Error += LocalFileManager_Error;
        _localFileManagerTask = _localFileManager.StartAsync(_cancellation.Token);
    }

    private void LocalFileManager_Error(Uri mediaUri, Exception e) =>
        _data.OnAddString(
            null,
            $"\n{string.Format(R.FileLoadError, Path.GetFileName(mediaUri.ToString()))}: {e.Message}\n",
            LogMode.Log);

    private void TInfo_MediaLoad() => _viewerActions.SendMessage(Messages.MediaLoaded);

    private void TInfo_MediaLoadError(Exception exc)
    {
        string error;

        if (exc is NotSupportedException)
        {
            error = $"{_localizer[nameof(R.MediaFileNotSupported)]}: {exc.Message}";
        }
        else
        {
            error = exc.ToString();
        }

        _data.OnAddString(null, $"{_localizer[nameof(R.MediaLoadError)]} {TInfo.MediaSource?.Uri}: {error}{Environment.NewLine}", LogMode.Log);
    }

    private void TInfo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableInfoViewModel.TStage))
        {
            if (TInfo.TStage == TableStage.RoundTable)
            {
                _data.BackLink.PlaySound();
            }
        }
    }

    #region ViewerInterface Members

    public virtual void ReceiveText(Message m)
    {
        _data.AddToChat(m);

        if (_data.BackLink.MakeLogs)
        {
            AddToFileLog(m);
        }
    }

    private static readonly XmlReaderSettings ReaderSettings = new() { ConformanceLevel = ConformanceLevel.Fragment };

    /// <summary>
    /// Вывод текста в протокол и в форму
    /// </summary>
    /// <param name="text">Выводимый текст</param>
    [Obsolete("Use OnReplic instead")]
    virtual public void Print(string text)
    {
        var chatMessageBuilder = new StringBuilder();
        var logMessageBuilder = new StringBuilder();

        var isPrintable = false;
        var special = false;

        try
        {
            using var reader = new StringReader(text);
            using var xmlReader = XmlReader.Create(reader, ReaderSettings);

            xmlReader.Read();
            while (!xmlReader.EOF)
            {
                if (xmlReader.NodeType == XmlNodeType.Element)
                {
                    ParseMessageToPrint(xmlReader, chatMessageBuilder, logMessageBuilder, ref isPrintable, ref special);
                }
                else
                {
                    xmlReader.Read();
                }
            }
        }
        catch (XmlException exc)
        {
            throw new Exception($"{_localizer[nameof(R.StringParseError)]} {text}.", exc);
        }

        var toFormStr = chatMessageBuilder.ToString();
        if (isPrintable)
        {
            var pair = toFormStr.Split(':');
            var speech = (pair.Length > 1 && pair[0].Length + 2 < toFormStr.Length) ? toFormStr[(pair[0].Length + 2)..] : toFormStr;

            if (_data.Speaker != null)
            {
                _data.Speaker.Replic = "";
            }

            _data.Speaker = _data.MainPersons.FirstOrDefault(item => item.Name == pair[0]);
            if (_data.Speaker != null)
            {
                _data.Speaker.Replic = speech.Trim();
            }
        }

        if (_data.BackLink.TranslateGameToChat || special)
        {
            _data.OnAddString(null, toFormStr.Trim(), LogMode.Protocol);
        }

        if (_data.BackLink.MakeLogs)
        {
            AddToFileLog(logMessageBuilder.ToString());
        }
    }

    /// <summary>
    /// Вывод сообщения в лог файл и в чат игры
    /// </summary>
    /// <param name="replicCode">ReplicCodes код сообщения или игрока</param>
    /// <param name="text">сообщение</param>
    public void OnReplic(string replicCode, string text)
    {
        string? logString = null;

        if (replicCode == ReplicCodes.Showman.ToString())
        {
            if (_data.ShowMan == null)
            {
                return;
            }

            // reset old speaker's replic
            if (_data.Speaker != null)
            {
                _data.Speaker.Replic = "";
            }

            // add new replic to the current speaker
            _data.Speaker = _data.ShowMan;
            _data.Speaker.Replic = TrimReplic(text);

            logString = $"<span class=\"sh\">{_data.Speaker.Name}: </span><span class=\"r\">{text}</span>";

            if (_data.BackLink.TranslateGameToChat)
            {
                _data.AddToChat(new Message(text, _data.Speaker.Name));
            }
        }
        else if (replicCode.StartsWith(ReplicCodes.Player.ToString()) && replicCode.Length > 1)
        {
            var indexString = replicCode[1..];

            if (int.TryParse(indexString, out var index) && index >= 0 && index < _data.Players.Count)
            {
                if (_data.Speaker != null)
                {
                    _data.Speaker.Replic = "";
                }

                _data.Speaker = _data.Players[index];
                _data.Speaker.Replic = TrimReplic(text);

                logString = $"<span class=\"sr n{index}\">{_data.Speaker.Name}: </span><span class=\"r\">{text}</span>";

                if (_data.BackLink.TranslateGameToChat)
                {
                    _data.AddToChat(new Message(text, _data.Speaker.Name));
                }
            }
        }
        else if (replicCode == ReplicCodes.Special.ToString())
        {
            logString = $"<span class=\"sp\">{text}</span>";
            _data.OnAddString("* ", text, LogMode.Protocol);
        }
        else
        {
            if (_data.BackLink.TranslateGameToChat)
            {
                _data.OnAddString(null, text, LogMode.Protocol);
            }

            // all other types of messages are printed only to logs
            logString = $"<span class=\"s\">{text}</span>";
        }

        if (logString != null && _data.BackLink.MakeLogs)
        {
            logString += "<br/>";
            AddToFileLog(logString);
        }
    }

    private string TrimReplic(string text) => text.Shorten(_data.BackLink.MaximumReplicTextLength, "…");

    internal void AddToFileLog(Message message) =>
        AddToFileLog(
            $"<span style=\"color: gray; font-weight: bold\">{message.Sender}:</span> " +
            $"<span style=\"font-weight: bold\">{message.Text}</span><br />");

    internal void AddToFileLog(string text)
    {
        if (_data.ProtocolWriter == null)
        {
            if (_data.ProtocolPath != null)
            {
                try
                {
                    var stream = _data.BackLink.CreateLog(_viewerActions.Client.Name, out var path);
                    _data.ProtocolPath = path;
                    _data.ProtocolWriter = new StreamWriter(stream);
                    _data.ProtocolWriter.Write(text);
                }
                catch (IOException)
                {
                }
            }
        }
        else
        {
            try
            {
                _data.ProtocolWriter.Write(text);
            }
            catch (IOException exc)
            {
                _data.OnAddString(null, $"{_localizer[nameof(R.ErrorWritingLogToDisc)]}: {exc.Message}", LogMode.Log);
                try
                {
                    _data.ProtocolWriter.Dispose();
                }
                catch
                {
                    // Из-за недостатка места на диске плохо закрывается
                }

                _data.ProtocolPath = null;
                _data.ProtocolWriter = null;
            }
            catch (EncoderFallbackException exc)
            {
                _data.OnAddString(null, $"{_localizer[nameof(R.ErrorWritingLogToDisc)]}: {exc.Message}", LogMode.Log);
            }
        }
    }

    [Obsolete]
    private void ParseMessageToPrint(
        XmlReader reader,
        StringBuilder chatMessageBuilder,
        StringBuilder logMessageBuilder,
        ref bool isPrintable,
        ref bool special)
    {
        var name = reader.Name;
        var content = reader.ReadElementContentAsString();

        switch (name)
        {
            case "this.client":
                chatMessageBuilder.AppendFormat("{0}: ", content);
                logMessageBuilder.AppendFormat("<span class=\"l\">{0}: </span>", content);
                break;

            case Constants.Player:
                {
                    isPrintable = true;

                    if (int.TryParse(content, out var playerIndex))
                    {
                        var speaker = $"<span class=\"sr n{playerIndex}\">";

                        var playerName = playerIndex < _data.Players.Count ? _data.Players[playerIndex].Name : "<" + _localizer[nameof(R.UnknownPerson)] + ">";
                        chatMessageBuilder.AppendFormat("{0}: ", playerName);
                        logMessageBuilder.AppendFormat("{0}{1}: </span>", speaker, playerName);
                    }
                }
                break;

            case Constants.Showman:
                isPrintable = true;
                chatMessageBuilder.AppendFormat("{0}: ", content);
                logMessageBuilder.AppendFormat("<span class=\"sh\">{0}: </span>", content);
                break;

            case "replic":
                chatMessageBuilder.Append(content);
                logMessageBuilder.AppendFormat("<span class=\"r\">{0}</span>", content);
                break;

            case "system":
                chatMessageBuilder.Append(content);
                logMessageBuilder.AppendFormat("<span class=\"s\">{0}</span>", content);
                break;

            case "special":
                special = true;
                chatMessageBuilder.AppendFormat("* {0}", content.ToUpper());
                logMessageBuilder.AppendFormat("<span class=\"sl\">{0}</span>", content);
                break;

            case "line":
                chatMessageBuilder.Append('\r');
                logMessageBuilder.Append("<br />");
                break;
        }
    }

    public void Stage()
    {
        switch (_data.Stage)
        {
            case GameStage.Before:
                break;

            case GameStage.Begin:
                TInfo.TStage = TableStage.Sign;

                if (_data.BackLink.MakeLogs && _data.ProtocolWriter == null)
                {
                    try
                    {
                        var stream = _data.BackLink.CreateLog(_viewerActions.Client.Name, out string path);
                        _data.ProtocolPath = path;
                        _data.ProtocolWriter = new StreamWriter(stream);
                        _data.ProtocolWriter.Write("<!DOCTYPE html><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\"/><title>" + _localizer[nameof(R.LogTitle)] + "</title>");
                        _data.ProtocolWriter.Write("<style>.sr { font-weight:bold; color: #00FFFF; } .n0 { color: #EF21A9; } .n1 { color: #0BE6CF; } .n2 { color: #EF9F21; } .n3 { color: #FF0000; } .n4 { color: #00FF00; } .n5 { color: #0000FF; } .sp, .sl { font-style: italic; font-weight: bold; } .sh { color: #0AEA2A; font-weight: bold; } .l { color: #646464; font-weight: bold; } .r { font-weight: bold; } .s { font-style: italic; } </style>");
                        _data.ProtocolWriter.Write("</head><body>");
                    }
                    catch (IOException)
                    {

                    }
                    catch (ArgumentException exc)
                    {
                        _data.BackLink.OnError(exc);
                    }
                    catch (UnauthorizedAccessException exc)
                    {
                        _data.BackLink.OnError(exc);
                    }
                }

                OnReplic(ReplicCodes.Special.ToString(), $"{_localizer[nameof(R.GameStarted)]} {DateTime.Now}");

                var gameMeta = new StringBuilder($"<span data-tag=\"gameInfo\" data-showman=\"{ClientData.ShowMan?.Name}\"");

                for (var i = 0; i < ClientData.Players.Count; i++)
                {
                    gameMeta.Append($" data-player-{i}=\"{ClientData.Players[i].Name}\"");
                }

                AddToFileLog(gameMeta + "></span>");
                break;

            case GameStage.Round:
            case GameStage.Final:
                TInfo.TStage = TableStage.Round;
                _data.Sound = Sounds.RoundBegin;

                foreach (var item in _data.Players)
                {
                    item.State = PlayerState.None;
                    item.Stake = 0;

                    item.SafeStake = false;
                }
                break;

            case GameStage.After:
                if (_data.ProtocolWriter != null)
                {
                    _data.ProtocolWriter.Write("</body></html>");
                }
                else
                {
                    _data.OnAddString(null, _localizer[nameof(R.ErrorWritingLogs)], LogMode.Chat);
                }
                break;

            default:
                break;
        }

    }

    virtual public void GameThemes()
    {
        TInfo.TStage = TableStage.GameThemes;
        _data.EnableMediaLoadButton = false;
    }

    virtual public void RoundThemes(bool print) => UI.Execute(() => RoundThemesUI(print), exc => _data.BackLink.SendError(exc));

    private void RoundThemesUI(bool print)
    {
        lock (_data.TInfoLock)
        lock (TInfo.RoundInfoLock)
        {
            TInfo.RoundInfo.Clear();

            foreach (var item in _data.TInfo.RoundInfo.Select(themeInfo => new ThemeInfoViewModel(themeInfo)))
            {
                TInfo.RoundInfo.Add(item);
            }
        }

        if (print)
        {
            if (_data.Stage == GameStage.Round)
            {
                TInfo.TStage = TableStage.RoundThemes;
                _data.BackLink.PlaySound(Sounds.RoundThemes);
            }
            else
            {
                TInfo.TStage = TableStage.Final;
            }
        }
    }

    virtual public async void Choice()
    {
        TInfo.Text = "";
        TInfo.MediaSource = null;
        TInfo.QuestionContentType = QuestionContentType.Text;
        TInfo.Sound = false;

        foreach (var item in _data.Players)
        {
            item.State = PlayerState.None;
        }

        var select = false;

        lock (_data.ChoiceLock)
        {
            lock (TInfo.RoundInfoLock)
            {
                if (_data.ThemeIndex > -1 &&
                    _data.ThemeIndex < TInfo.RoundInfo.Count &&
                    _data.QuestionIndex > -1 &&
                    _data.QuestionIndex < TInfo.RoundInfo[_data.ThemeIndex].Questions.Count)
                {
                    select = true;
                }
            }
        }

        if (!select)
        {
            return;
        }

        try
        {
            await TInfo.PlaySimpleSelectionAsync(_data.ThemeIndex, _data.QuestionIndex);
        }
        catch (Exception exc)
        {
            _viewerActions.Client.CurrentServer.OnError(exc, false);
        }
    }

    public void TextShape(string[] mparams)
    {
        var text = new StringBuilder();

        for (var i = 1; i < mparams.Length; i++)
        {
            text.Append(mparams[i]);

            if (i < mparams.Length - 1)
            {
                text.Append('\n');
            }
        }

        if (!TInfo.PartialText && TInfo.TStage == TableStage.Question)
        {
            // Toggle TStage change to reapply QuestionTemplateSelector template
            TInfo.TStage = TableStage.Void;
        }

        TInfo.TextLength = 0;
        TInfo.PartialText = true;
        TInfo.Text = text.ToString();
        TInfo.TStage = TableStage.Question;
    }

    virtual public void OnScreenContent(string[] mparams)
    {
        if (TInfo.TStage != TableStage.Answer && _data.Speaker != null && !_data.Speaker.IsShowman)
        {
            _data.Speaker.Replic = "";
        }

        _data.AtomType = mparams[1];

        var isPartial = _data.AtomType == Constants.PartialText;

        if (isPartial)
        {
            if (!_data.IsPartial)
            {
                _data.IsPartial = true;
                _data.AtomIndex++;
            }
        }
        else
        {
            _data.IsPartial = false;
            _data.AtomIndex++;

            if (_data.AtomType != AtomTypes.Oral)
            {
                TInfo.Text = "";
            }

            TInfo.PartialText = false;
        }

        TInfo.TStage = TableStage.Question;
        TInfo.IsMediaStopped = false;

        _data.EnableMediaLoadButton = false;

        switch (_data.AtomType)
        {
            case AtomTypes.Text:
            case Constants.PartialText:
                var text = new StringBuilder();

                for (var i = 2; i < mparams.Length; i++)
                {
                    text.Append(mparams[i]);

                    if (i < mparams.Length - 1)
                    {
                        text.Append('\n');
                    }
                }

                if (isPartial)
                {
                    var currentText = TInfo.Text ?? "";
                    var newTextLength = text.Length;

                    var tailIndex = TInfo.TextLength + newTextLength;

                    TInfo.Text = currentText[..TInfo.TextLength]
                        + text
                        + (currentText.Length > tailIndex ? currentText[tailIndex..] : "");

                    TInfo.TextLength += newTextLength;
                }
                else
                {
                    TInfo.Text = text.ToString().Shorten(_data.BackLink.MaximumTableTextLength, "…");
                }

                TInfo.QuestionContentType = QuestionContentType.Text;
                TInfo.Sound = false;
                _data.EnableMediaLoadButton = false;
                break;

            case AtomTypes.Video:
            case AtomTypes.Audio:
            case AtomTypes.AudioNew:
            case AtomTypes.Image:
            case AtomTypes.Html:
                string uri;

                switch (mparams[2])
                {
                    case MessageParams.Atom_Uri:
                        uri = mparams[3];

                        if (uri.Contains(Constants.GameHost))
                        {
                            var address = ClientData.ServerAddress;

                            if (!string.IsNullOrWhiteSpace(address))
                            {
                                if (Uri.TryCreate(address, UriKind.Absolute, out var hostUri))
                                {
                                    uri = uri.Replace(Constants.GameHost, hostUri.Host);
                                }
                            }
                        }
                        else if (uri.Contains(Constants.ServerHost))
                        {
                            uri = uri.Replace(Constants.ServerHost, ClientData.ServerPublicUrl ?? ClientData.ServerAddress);
                        }
                        else if (_data.AtomType != AtomTypes.Html
                            && !uri.StartsWith("http://localhost")
                            && !Data.BackLink.LoadExternalMedia
                            && !ExternalUrlOk(uri))
                        {
                            TInfo.Text = string.Format(_localizer[nameof(R.ExternalLink)], uri);
                            TInfo.QuestionContentType = QuestionContentType.SpecialText;
                            TInfo.Sound = false;

                            _data.EnableMediaLoadButton = true;
                            _data.ExternalUri = uri;
                            return;
                        }

                        break;

                    default:
                        return;
                }

                if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var mediaUri))
                {
                    return;
                }

                uri = _localFileManager.TryGetFile(mediaUri) ?? uri;

                if (_data.AtomType == AtomTypes.Image)
                {
                    TInfo.MediaSource = new MediaSource(uri);
                    TInfo.QuestionContentType = QuestionContentType.Image;
                    TInfo.Sound = false;
                }
                else if (_data.AtomType == AtomTypes.Audio || _data.AtomType == AtomTypes.AudioNew)
                {
                    TInfo.SoundSource = new MediaSource(uri);
                    TInfo.QuestionContentType = QuestionContentType.Clef;
                    TInfo.Sound = true;
                }
                else if (_data.AtomType == AtomTypes.Video)
                {
                    TInfo.MediaSource = new MediaSource(uri);
                    TInfo.QuestionContentType = QuestionContentType.Video;
                    TInfo.Sound = false;
                }
                else
                {
                    TInfo.MediaSource = new MediaSource(uri);
                    TInfo.QuestionContentType = QuestionContentType.Html;
                    TInfo.Sound = false;
                }

                _data.EnableMediaLoadButton = false;
                break;
        }
    }

    public void ReloadMedia()
    {
        _data.EnableMediaLoadButton = false;

        switch (_data.AtomType)
        {
            case AtomTypes.Image:
                TInfo.MediaSource = new MediaSource(_data.ExternalUri);
                TInfo.QuestionContentType = QuestionContentType.Image;
                TInfo.Sound = false;
                break;

            case AtomTypes.Audio:
            case AtomTypes.AudioNew:
                TInfo.SoundSource = new MediaSource(_data.ExternalUri);
                TInfo.QuestionContentType = QuestionContentType.Clef;
                TInfo.Sound = true;
                break;

            case AtomTypes.Video:
                TInfo.MediaSource = new MediaSource(_data.ExternalUri);
                TInfo.QuestionContentType = QuestionContentType.Video;
                TInfo.Sound = false;
                break;
        }
    }

    private bool ExternalUrlOk(string uri) =>
        ClientData.ContentPublicUrls != null && ClientData.ContentPublicUrls.Any(publicUrl => uri.StartsWith(publicUrl));

    virtual public void OnBackgroundContent(string[] mparams)
    {
        if (TInfo.TStage != TableStage.Question)
        {
            TInfo.TStage = TableStage.Question;
            TInfo.QuestionContentType = QuestionContentType.Clef;
        }

        var atomType = mparams[1];

        switch (atomType)
        {
            case AtomTypes.Audio:
            case AtomTypes.AudioNew:
                string uri;

                switch (mparams[2])
                {
                    case MessageParams.Atom_Uri:
                        uri = mparams[3];

                        if (uri.Contains(Constants.GameHost))
                        {
                            var address = ClientData.ServerAddress;

                            if (!string.IsNullOrWhiteSpace(address))
                            {
                                if (Uri.TryCreate(address, UriKind.Absolute, out var hostUri))
                                {
                                    uri = uri.Replace(Constants.GameHost, hostUri.Host);
                                }
                            }
                        }
                        else if (uri.Contains(Constants.ServerHost))
                        {
                            uri = uri.Replace(Constants.ServerHost, ClientData.ServerPublicUrl ?? ClientData.ServerAddress);
                        }
                        else if (!uri.StartsWith("http://localhost") && !Data.BackLink.LoadExternalMedia && !ExternalUrlOk(uri))
                        {
                            TInfo.Text = string.Format(_localizer[nameof(R.ExternalLink)], uri);
                            TInfo.QuestionContentType = QuestionContentType.SpecialText;
                            TInfo.Sound = false;

                            _data.EnableMediaLoadButton = true;
                            _data.ExternalUri = uri;
                        }

                        break;

                    default:
                        return;
                }

                Uri? mediaUri;

                if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out mediaUri))
                {
                    return;
                }

                uri = _localFileManager.TryGetFile(mediaUri) ?? uri;

                TInfo.SoundSource = new MediaSource(uri);
                TInfo.Sound = true;

                if (TInfo.QuestionContentType == QuestionContentType.Void)
                {
                    TInfo.QuestionContentType = QuestionContentType.Clef;
                }

                break;
        }
    }

    virtual public void OnAtomHint(string hint)
    {
        TInfo.Hint = hint;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(HintLifetime);
                TInfo.Hint = "";
            }
            catch (Exception exc)
            {
                _data.BackLink.SendError(exc);
            }
        });
    }

    public void SetRight(string answer)
    {
        try
        {
            TInfo.TStage = TableStage.Answer;
            TInfo.Text = answer;
            _data.EnableMediaLoadButton = false;
        }
        catch (NullReferenceException exc)
        {
            // Странная ошибка в привязках WPF иногда возникает
            _data.BackLink.SendError(exc);
        }
    }

    public void Try() => TInfo.QuestionStyle = QuestionStyle.WaitingForPress;

    /// <summary>
    /// Нельзя жать на кнопку
    /// </summary>
    /// <param name="text">Кто уже нажал или время вышло</param>
    virtual public void EndTry(string text)
    {
        TInfo.QuestionStyle = QuestionStyle.Normal;

        if (_data.AtomType == AtomTypes.Audio || _data.AtomType == AtomTypes.AudioNew || _data.AtomType == AtomTypes.Video)
        {
            TInfo.IsMediaStopped = true;
        }

        if (!int.TryParse(text, out int number))
        {
            _data.Sound = Sounds.QuestionNoAnswers;
            return;
        }

        if (number < 0 || number >= _data.Players.Count)
        {
            return;
        }

        _data.Players[number].State = PlayerState.Press;
    }

    virtual public void ShowTablo() => TInfo.TStage = TableStage.RoundTable;

    /// <summary>
    /// Игрок получил или потерял деньги
    /// </summary>
    virtual public void Person(int playerIndex, bool isRight)
    {
        if (isRight)
        {
            _data.Sound = _data.CurPriceRight >= 2000 ? Sounds.ApplauseBig : Sounds.ApplauseSmall;
            _data.Players[playerIndex].State = PlayerState.Right;
        }
        else
        {
            _data.Sound = Sounds.AnswerWrong;
            _data.Players[playerIndex].Pass = true;
            _data.Players[playerIndex].State = PlayerState.Wrong;
        }

        AddToFileLog($"<span data-tag=\"sumChange\" data-playerIndex=\"{playerIndex}\" data-change=\"{(isRight ? 1 : -1) * ClientData.CurPriceRight}\"></span>");
    }

    public void OnPersonFinalStake(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= _data.Players.Count)
        {
            return;
        }

        _data.Players[playerIndex].Stake = -4;
    }

    public void OnPersonFinalAnswer(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= _data.Players.Count)
        {
            return;
        }

        _data.Players[playerIndex].State = PlayerState.HasAnswered;
    }

    public void OnPersonApellated(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= _data.Players.Count)
        {
            return;
        }

        _data.Players[playerIndex].State = PlayerState.HasAnswered;
    }

    public void OnQuestionType()
    {
        TInfo.QuestionContentType = QuestionContentType.Text;
        TInfo.Sound = false;

        switch (_data.QuestionType)
        {
            case QuestionTypes.Auction:
            case QuestionTypes.Stake:
                {
                    TInfo.Text = _localizer[nameof(R.Label_Auction)];

                    lock (TInfo.RoundInfoLock)
                    {
                        for (int i = 0; i < TInfo.RoundInfo.Count; i++)
                        {
                            TInfo.RoundInfo[i].Active = i == _data.ThemeIndex;
                        }
                    }

                    TInfo.TStage = TableStage.Special;
                    _data.Sound = Sounds.QuestionStake;
                    break;
                }

            case QuestionTypes.Cat:
            case QuestionTypes.BagCat:
            case QuestionTypes.Secret:
            case QuestionTypes.SecretNoQuestion:
            case QuestionTypes.SecretPublicPrice:
                {
                    TInfo.Text = _localizer[nameof(R.Label_CatInBag)];

                    lock (TInfo.RoundInfoLock)
                    {
                        foreach (var item in TInfo.RoundInfo)
                        {
                            item.Active = false;
                        }
                    }

                    TInfo.TStage = TableStage.Special;
                    _data.Sound = Sounds.QuestionSecret;
                    break;
                }

            case QuestionTypes.Sponsored:
            case QuestionTypes.NoRisk:
                {
                    TInfo.Text = _localizer[nameof(R.Label_Sponsored)];

                    lock (TInfo.RoundInfoLock)
                    {
                        foreach (var item in TInfo.RoundInfo)
                        {
                            item.Active = false;
                        }
                    }

                    TInfo.TStage = TableStage.Special;
                    _data.Sound = Sounds.QuestionNoRisk;
                    break;
                }

            case QuestionTypes.Simple:
                TInfo.TimeLeft = 1.0;
                break;

            default:
                foreach (var item in TInfo.RoundInfo)
                {
                    item.Active = false;
                }
                break;
        }
    }

    public void StopRound() => TInfo.TStage = TableStage.Sign;

    virtual public void Out(int themeIndex)
    {
        TInfo.PlaySelection(themeIndex);
        _data.Sound = Sounds.FinalDelete;
    }

    public void Winner() => UI.Execute(WinnerUI, exc => _data.BackLink.SendError(exc));

    private void WinnerUI()
    {
        if (_data.Winner > -1)
        {
            _data.Sound = Sounds.ApplauseFinal;
        }

        // Лучшие игроки
        _data.BackLink.SaveBestPlayers(_data.Players);
    }

    public void TimeOut() => _data.Sound = Sounds.RoundTimeout;

    protected override async ValueTask DisposeAsync(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_data.ProtocolWriter != null)
        {
            _data.ProtocolWriter.Dispose();
            _data.ProtocolWriter = null;
        }

        _cancellation.Cancel();

        await _localFileManagerTask;

        _localFileManager.Dispose();
        _cancellation.Dispose();

        await base.DisposeAsync(disposing);
    }

    public void FinalThink() => _data.BackLink.PlaySound(Sounds.FinalThink);

    public void UpdatePicture(Account account, string path)
    {
        if (path.Contains(Constants.GameHost))
        {
            if (!string.IsNullOrWhiteSpace(ClientData.ServerAddress))
            {
                var remoteUri = ClientData.ServerAddress;

                if (Uri.TryCreate(remoteUri, UriKind.Absolute, out var hostUri))
                {
                    account.Picture = path.Replace(Constants.GameHost, hostUri.Host);
                }
                else
                {
                    // Блок для отлавливания специфической ошибки
                    _data.BackLink.OnPictureError(remoteUri);
                }
            }
        }
        else if (path.Contains(Constants.ServerHost))
        {
            if (!string.IsNullOrWhiteSpace(ClientData.ServerAddress))
            {
                account.Picture = path.Replace(Constants.ServerHost, ClientData.ServerPublicUrl ?? ClientData.ServerAddress);
            }
        }
        else
        {
            account.Picture = path;
        }
    }

    /// <summary>
    /// Попытка осуществить повторное подключение к серверу
    /// </summary>
    public async void TryConnect(IConnector connector)
    {
        try
        {
            OnReplic(ReplicCodes.Special.ToString(), _localizer[nameof(R.TryReconnect)]);

            var result = await connector.ReconnectToServer();

            if (!result)
            {
                AnotherTry(connector);
                return;
            }

            OnReplic(ReplicCodes.Special.ToString(), _localizer[nameof(R.ReconnectOK)]);
            await connector.RejoinGame();

            if (!string.IsNullOrEmpty(connector.Error))
            {
                if (connector.CanRetry)
                {
                    AnotherTry(connector);
                }
                else
                {
                    OnReplic(ReplicCodes.Special.ToString(), connector.Error);
                }
            }
            else
            {
                OnReplic(ReplicCodes.Special.ToString(), _localizer[nameof(R.ReconnectEntered)]);
            }
        }
        catch (Exception exc)
        {
            try { _data.BackLink.OnError(exc); }
            catch { }
        }
    }

    private async void AnotherTry(IConnector connector)
    {
        try
        {
            OnReplic(ReplicCodes.Special.ToString(), connector.Error);

            if (!_disposed)
            {
                await Task.Delay(10000);
                TryConnect(connector);
            }
        }
        catch (Exception exc)
        {
            Trace.TraceError("AnotherTry error: " + exc);
        }
    }

    #endregion

    public void OnTextSpeed(double speed) => TInfo.TextSpeed = speed;

    public void SetText(string text, TableStage stage = TableStage.Round)
    {
        TInfo.Text = text;
        TInfo.TStage = stage;
        _data.EnableMediaLoadButton = false;
    }

    public void OnPauseChanged(bool isPaused) => TInfo.Pause = isPaused;

    public void TableLoaded() => UI.Execute(TableLoadedUI, exc => _data.BackLink.SendError(exc));

    private void TableLoadedUI()
    {
        lock (TInfo.RoundInfoLock)
        {
            for (int i = 0; i < _data.TInfo.RoundInfo.Count; i++)
            {
                if (TInfo.RoundInfo.Count <= i)
                    break;

                TInfo.RoundInfo[i].Questions.Clear();

                foreach (var item in _data.TInfo.RoundInfo[i].Questions.Select(questionInfo => new QuestionInfoViewModel(questionInfo)).ToArray())
                {
                    TInfo.RoundInfo[i].Questions.Add(item);
                }
            }
        }
    }

    public void Resume() => TInfo.IsMediaStopped = false;

    public async void PrintGreeting()
    {
        try
        {
            await Task.Delay(1000);
            _data.OnAddString(null, _localizer[nameof(R.Greeting)] + Environment.NewLine, LogMode.Protocol);
        }
        catch (Exception exc)
        {
            Trace.TraceError("PrintGreeting error: " + exc);
        }
    }

    public void OnTimeChanged()
    {
        
    }

    public void OnTimerChanged(int timerIndex, string timerCommand, string arg, string person)
    {
        if (timerIndex == 1 && timerCommand == "RESUME")
        {
            TInfo.QuestionStyle = QuestionStyle.WaitingForPress;
        }

        if (timerIndex != 2)
        {
            return;
        }

        switch (timerCommand)
        {
            case MessageParams.Timer_Go:
                {
                    if (person != null && int.TryParse(person, out int personIndex))
                    {
                        if (_data.DialogMode == DialogModes.ChangeSum
                            || _data.DialogMode == DialogModes.Manage
                            || _data.DialogMode == DialogModes.None)
                        {
                            if (personIndex == -1)
                            {
                                if (_data.ShowMan != null)
                                {
                                    _data.ShowMan.IsDeciding = true;
                                }
                            }
                            else if (personIndex > -1 && personIndex < _data.Players.Count)
                            {
                                _data.Players[personIndex].IsDeciding = true;
                            }
                        }

                        if (personIndex == -2)
                        {
                            _data.ShowMainTimer = true;
                        }
                    }

                    break;
                }

            case MessageParams.Timer_Stop:
                {
                    if (_data.ShowMan != null)
                    {
                        _data.ShowMan.IsDeciding = false;
                    }

                    foreach (var player in _data.Players)
                    {
                        player.IsDeciding = false;
                    }

                    _data.ShowMainTimer = false;
                    break;
                }
        }
    }

    public void OnPackageLogo(string uri)
    {
        TInfo.TStage = TableStage.Question;

        if (uri.Contains(Constants.GameHost))
        {
            var address = ClientData.ServerAddress;

            if (!string.IsNullOrWhiteSpace(address))
            {
                if (Uri.TryCreate(address, UriKind.Absolute, out var hostUri))
                {
                    uri = uri.Replace(Constants.GameHost, hostUri.Host);
                }
            }
        }
        else if (uri.Contains(Constants.ServerHost))
        {
            uri = uri.Replace(Constants.ServerHost, ClientData.ServerPublicUrl ?? ClientData.ServerAddress);
        }

        if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out _))
        {
            return;
        }

        TInfo.MediaSource = new MediaSource(uri);
        TInfo.QuestionContentType = QuestionContentType.Image;
        TInfo.Sound = false;
    }

    public void OnPersonPass(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= _data.Players.Count)
        {
            return;
        }

        _data.Players[playerIndex].State = PlayerState.Pass;
    }

    public void OnRoundContent(string[] mparams)
    {
        for (var i = 1; i < mparams.Length; i++)
        {
            var uri = mparams[i];

            if (uri.Contains(Constants.GameHost))
            {
                var address = ClientData.ServerAddress;

                if (!string.IsNullOrWhiteSpace(address))
                {
                    if (Uri.TryCreate(address, UriKind.Absolute, out var hostUri))
                    {
                        uri = uri.Replace(Constants.GameHost, hostUri.Host);
                    }
                }
            }
            else if (uri.Contains(Constants.ServerHost))
            {
                uri = uri.Replace(Constants.ServerHost, ClientData.ServerPublicUrl ?? ClientData.ServerAddress);
            }
            else if (!uri.StartsWith("http://localhost") && !Data.BackLink.LoadExternalMedia && !ExternalUrlOk(uri))
            {
                continue;
            }

            if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var mediaUri))
            {
                continue;
            }

            _localFileManager.AddFile(mediaUri);
        }
    }

    private void OnSpecialReplic(string message) => OnReplic(ReplicCodes.Special.ToString(), message);

    public void OnUnbanned(string ip) =>
        UI.Execute(
            () =>
            {
                var banned = ClientData.Banned.FirstOrDefault(p => p.Ip == ip);

                if (banned != null)
                {
                    ClientData.Banned.Remove(banned);
                    OnSpecialReplic(string.Format(_localizer[nameof(R.UserUnbanned)], banned.UserName));
                }
            },
            exc => ClientData.BackLink.OnError(exc));

    public void OnBanned(BannedInfo bannedInfo) =>
        UI.Execute(
            () =>
            {
                ClientData.Banned.Add(bannedInfo);
            },
            exc => ClientData.BackLink.OnError(exc));

    public void OnBannedList(IEnumerable<BannedInfo> banned) =>
        UI.Execute(() =>
        {
            ClientData.Banned.Clear();

            foreach (var item in banned)
            {
                ClientData.Banned.Add(item);
            }
        },
        exc => ClientData.BackLink.OnError(exc));

    public void SetCaption(string caption) => TInfo.Caption = caption;

    public void OnGameMetadata(string gameName, string packageName, string contactUri, string voiceChatUri)
    {
        var gameInfo = new StringBuilder();

        var coersedGameName = gameName.Length > 0 ? gameName : R.LocalGame;

        gameInfo.AppendFormat(R.GameName).Append(": ").Append(coersedGameName).AppendLine();
        gameInfo.AppendFormat(R.PackageName).Append(": ").Append(packageName).AppendLine();
        gameInfo.AppendFormat(R.ContactUri).Append(": ").Append(contactUri).AppendLine();
        gameInfo.AppendFormat(R.VoiceChatLink).Append(": ").Append(voiceChatUri).AppendLine();

        ClientData.GameMetadata = gameInfo.ToString();

        if (!string.IsNullOrEmpty(voiceChatUri) && Uri.IsWellFormedUriString(voiceChatUri, UriKind.Absolute))
        {
            ClientData.VoiceChatUri = voiceChatUri;
        }
    }

    public void AddPlayer(PlayerAccount account) => UI.Execute(
        () =>
        {
            ClientData.PlayersObservable.Add(account);
        },
        ClientData.BackLink.OnError);

    public void RemovePlayerAt(int index) => UI.Execute(
        () =>
        {
            ClientData.PlayersObservable.RemoveAt(index);
        },
        ClientData.BackLink.OnError);

    public void ResetPlayers() => UI.Execute(
        () =>
        {
            ClientData.PlayersObservable.Clear();

            foreach (var player in ClientData.Players)
            {
                ClientData.PlayersObservable.Add(player);
            }
        },
        ClientData.BackLink.OnError);
}
