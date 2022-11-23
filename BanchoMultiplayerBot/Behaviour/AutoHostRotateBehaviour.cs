﻿using BanchoSharp;
using BanchoSharp.Interfaces;
using BanchoSharp.Multiplayer;

namespace BanchoMultiplayerBot.Behaviour;

/// <summary>
/// This behaviour will manage a queue and pass the host around, so everyone gets a chance to 
/// pick a map. 
/// </summary>
public class AutoHostRotateBehaviour : IBotBehaviour
{
    private Lobby _lobby = null!;
    private PlayerVote _playerSkipVote = null!;

    private bool _hasSkippedHost = false;

    public List<string> Queue { get; } = new();

    public void Setup(Lobby lobby)
    {
        _lobby = lobby;
        _playerSkipVote = new PlayerVote(_lobby, "Skip host vote");

        _lobby.MultiplayerLobby.OnPlayerJoined += player =>
        {
            Logger.Trace("AutoHostRotateBehaviour::OnPlayerJoined()");

            if (!Queue.Contains(player.Name))
                Queue.Add(player.Name);

            if (_lobby.IsRecovering)
            {
                return;
            }

            OnQueueUpdated();
        };

        _lobby.MultiplayerLobby.OnPlayerDisconnected += disconnectEventArgs =>
        {
            Logger.Trace("AutoHostRotateBehaviour::OnPlayerDisconnected()");

            if (Queue.Contains(disconnectEventArgs.Player.Name))
            {
                Queue.Remove(disconnectEventArgs.Player.Name);

                OnQueueUpdated();

                if (_lobby.MultiplayerLobby.Host is not null && _lobby.MultiplayerLobby.Host.Name == disconnectEventArgs.Player.Name && _lobby.MultiplayerLobby.MatchInProgress)
                {
                    _hasSkippedHost = true;
                }
            }
        };

        _lobby.MultiplayerLobby.OnMatchStarted += () =>
        {
            _hasSkippedHost = false;
        };

        _lobby.MultiplayerLobby.OnSettingsUpdated += OnSettingsUpdated;
        _lobby.MultiplayerLobby.OnHostChanged += OnHostChanged;
        _lobby.OnUserMessage += OnUserMessage;
        _lobby.OnAdminMessage += OnAdminMessage;
    }

    /// <summary>
    /// Skips the current host and makes the next player in the queue host instead.
    /// </summary>
    public void ForceSkipPlayer()
    {
        SkipCurrentPlayer();
        OnQueueUpdated();
    }

    private void OnAdminMessage(IPrivateIrcMessage message)
    {
        if (message.Content.StartsWith("!forceskip"))
        {
            ForceSkipPlayer();
        }

        if (message.Content.StartsWith("!sethost "))
        {
            try
            {
                var name = message.Content.Split("!sethost ")[1];

                if (Queue.Contains(name))
                {
                    Queue.Remove(name);
                }

                Queue.Insert(0, name);

                OnQueueUpdated();
            }
            catch (Exception)
            {
            }
        }
    }

    private void OnUserMessage(IPrivateIrcMessage message)
    {
        Logger.Trace("AutoHostRotateBehaviour::OnUserMessage()");

        // Allow the users to see the current queue
        if (message.Content.StartsWith("!q") || message.Content.StartsWith("!queue"))
        {
            SendCurrentQueue();

            return;
        }

        if (message.Content.StartsWith("!skip"))
        {
            // If the host is sending the message, just skip.
            if (_lobby.MultiplayerLobby.Host is not null)
            {
                if (message.Sender == _lobby.MultiplayerLobby.Host.Name)
                {
                    SkipCurrentPlayer();

                    OnQueueUpdated();

                    return;
                }
            }

            // If the player isn't host, start a vote.
            var player = _lobby.MultiplayerLobby.Players.FirstOrDefault(x => x.Name == message.Sender);
            if (player is not null)
            {
                if (_playerSkipVote.Vote(player))
                {
                    SkipCurrentPlayer();

                    OnQueueUpdated();

                    return;
                }
            }
        }
    }

    private void OnSettingsUpdated()
    {
        Logger.Trace("AutoHostRotateBehaviour::OnSettingsUpdated()");

        // Attempt to reload the old queue if we're recovering a previous session.
        if (_lobby.IsRecovering && _lobby.Configuration.PreviousQueue != null)
        {
            Queue.Clear();

            foreach (var player in _lobby.Configuration.PreviousQueue.Split(','))
            {
                if (_lobby.MultiplayerLobby.Players.FirstOrDefault(x => x.Name == player) is not null && !Queue.Contains(player))
                {
                    Queue.Add(player);
                }
            }

            Console.WriteLine($"Recovered old queue: {string.Join(", ", Queue.Take(5))}");
        }

        foreach (var player in _lobby.MultiplayerLobby.Players)
        {
            if (!Queue.Contains(player.Name))
                Queue.Add(player.Name);
        }

        // Don't skip a player if we're just restoring a previous session.
        if (_lobby.IsRecovering)
        {
            return;
        }

        if (!_hasSkippedHost)
            SkipCurrentPlayer();

        OnQueueUpdated();
        SendCurrentQueue();
    }

    private void OnHostChanged(MultiplayerPlayer player)
    {
        Logger.Trace("AutoHostRotateBehaviour::OnHostChanged()");

        if (!Queue.Any()) return;

        if (_lobby.IsRecovering)
            return;

        if (player.Name != Queue[0])
        {
            _lobby.SendMessage($"!mp host {Queue[0]}");
        }
    }

    private void OnQueueUpdated()
    {
        Logger.Trace("AutoHostRotateBehaviour::OnQueueUpdated()");

        if (!Queue.Any()) return;

        if (_lobby.MultiplayerLobby.Host is null)
        {
            _lobby.SendMessage($"!mp host {Queue[0].Replace(' ', '_')}");
            return;
        }

        if (_lobby.MultiplayerLobby.Host.Name != Queue[0])
        {
            _lobby.SendMessage($"!mp host {Queue[0].Replace(' ', '_')}");
        }
    }

    /// <summary>
    /// Send the first 5 people in the queue in the lobby chat. The player names will include a 
    /// zero width space to avoid tagging people.
    /// </summary>
    private void SendCurrentQueue()
    {
        var cleanPlayerNamesQueue = new List<string>();

        // Add a zero width space to the player names to avoid mentioning them
        foreach (var playerName in Queue.Take(5))
        {
            cleanPlayerNamesQueue.Add($"{playerName[0]}\u200B{playerName.Substring(1)}");
        }

        var queueStr = string.Join(", ", cleanPlayerNamesQueue.Take(5));

        if (Queue.Count > 5)
            queueStr += "...";

        _lobby.SendMessage($"Queue: {queueStr}");
    }

    /// <summary>
    /// Skips the first user in the queue, will NOT automatically update host.
    /// </summary>
    private void SkipCurrentPlayer()
    {
        Logger.Trace("AutoHostRotateBehaviour::SkipCurrentPlayer()");

        if (!Queue.Any()) return;

        var playerName = Queue[0];

        Queue.RemoveAt(0);

        // Re-add him back to the end of the queue
        Queue.Add(playerName);

        _hasSkippedHost = false;
        _playerSkipVote.Reset();
    }
}