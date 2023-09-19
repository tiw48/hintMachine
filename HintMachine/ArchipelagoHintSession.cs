﻿using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using HintMachine.Games;
using System;
using System.Collections.Generic;
using System.Linq;
using static Archipelago.MultiClient.Net.Helpers.MessageLogHelper;

namespace HintMachine
{
    public class ArchipelagoHintSession
    {
        private static readonly string[] TAGS = { "AP", "TextOnly" };
        private static readonly Version VERSION = new Version(0, 4, 1);

        private ArchipelagoSession _session = null;
        public string host = "";
        public string slot = "";
        public bool isConnected = false;
        public string errorMessage = "";
        private List<long> _alreadyHintedLocations = new List<long>();

        public HintsView HintsView { get; set; } = null;

        public ArchipelagoHintSession(string host, string slot, string password)
        {
            this.host = host;
            this.slot = slot;
            _session = ArchipelagoSessionFactory.CreateSession(host);

            Console.WriteLine("Start Connect & Login");
            LoginResult ret;
            try
            {
                ret = _session.TryConnectAndLogin("", slot, ItemsHandlingFlags.AllItems, VERSION, TAGS, null, password, true);
            }
            catch (Exception ex)
            {
                ret = new LoginFailure(ex.GetBaseException().Message);
            }

            isConnected = ret.Successful;
            if (!isConnected)
            {
                LoginFailure loginFailure = (LoginFailure)ret;
                foreach (string str in loginFailure.Errors)
                {
                    errorMessage += "\n" + str;
                }
                foreach (ConnectionRefusedError connectionRefusedError in loginFailure.ErrorCodes)
                {
                    errorMessage += string.Format("\n{0}", connectionRefusedError);
                }
                return;
            }

            // Add a tracking event to detect further hints...
            _session.DataStorage.TrackHints(OnHintObtained);
            // ...and call that event a first time with all already obtained hints
            OnHintObtained(_session.DataStorage.GetHints());
        }

        public List<HintDetails> GetHints()
        {
            List<HintDetails> returned = new List<HintDetails>();
            Hint[] hints = _session.DataStorage.GetHints();

            foreach (Hint hint in hints)
            {
                if (hint.Found)
                    continue;

                returned.Add(new HintDetails
                {
                    ReceivingPlayer = hint.ReceivingPlayer,
                    FindingPlayer = hint.FindingPlayer,
                    ItemId = hint.ItemId,
                    LocationId = hint.LocationId,
                    ItemFlags = hint.ItemFlags,
                    Found = hint.Found,
                    Entrance = hint.Entrance,

                    ReceivingPlayerName = _session.Players.GetPlayerName(hint.ReceivingPlayer),
                    FindingPlayerName = _session.Players.GetPlayerName(hint.FindingPlayer),
                    ItemName = _session.Items.GetItemName(hint.ItemId),
                    LocationName = _session.Locations.GetLocationNameFromId(hint.LocationId),
                });
            }

            return returned;
        }

        public void GetOneRandomHint()
        {
            List<long> missingLocations = _session.Locations.AllMissingLocations.ToList();
            foreach (long locationId in _alreadyHintedLocations)
                missingLocations.Remove(locationId);

            if (missingLocations.Count == 0)
                return;

            Random rnd = new Random();
            int index = rnd.Next(missingLocations.Count);
            long hintedLocationId = _session.Locations.AllMissingLocations[index];

            _alreadyHintedLocations.Add(hintedLocationId);
            _session.Socket.SendPacket(new LocationScoutsPacket {
                Locations = new long[] { hintedLocationId },
                CreateAsHint = true
            });

            _session.DataStorage.GetHints();
        }

        public void OnHintObtained(Hint[] hints)
        {
            // Add the hints to the list of already known locations so that we won't 
            // try to give a random hint for those
            foreach (Hint hint in hints)
                if (hint.FindingPlayer == _session.ConnectionInfo.Slot)
                    _alreadyHintedLocations.Add(hint.LocationId);

            HintsView?.UpdateItems(GetHints());
        }

        public void Disconnect()
        {
            _session.Socket.DisconnectAsync();
        }

        public void SendMessage(string message)
        {
            _session.Socket.SendPacket(new SayPacket { Text = message });
        }

        public void SetupOnMessageReceivedEvent(MessageReceivedHandler handler)
        {
            _session.MessageLog.OnMessageReceived += handler;
        }
    }
}
