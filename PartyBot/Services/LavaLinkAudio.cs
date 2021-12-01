using Discord;
using Discord.WebSocket;
using PartyBot.Handlers;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Victoria;
using Victoria.EventArgs;
using Victoria.Enums;
using Victoria.Responses.Search;
using Victoria.Filters;
using System.Collections.Generic;
using PartyBot.DataStructs;

namespace PartyBot.Services
{
    public sealed class LavaLinkAudio
    {
        private readonly LavaNode _lavaNode;
        // Yay C# 10 implicit new
        private readonly Dictionary<IGuild, ServerData> _serverData = new();

        public LavaLinkAudio(LavaNode lavaNode)
            => _lavaNode = lavaNode;

        public async Task<Embed> JoinAsync(IGuild guild, IVoiceState voiceState, ITextChannel textChannel)
        {
            if (_lavaNode.HasPlayer(guild))
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Join", "I'm already connected to a voice channel!");
            }

            if (voiceState.VoiceChannel is null)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Join", "You must be connected to a voice channel!");
            }

            // Init the Server Data when the bot joins a VC.
            _serverData.Add(guild, new ServerData());

            try
            {
                await _lavaNode.JoinAsync(voiceState.VoiceChannel, textChannel);
                return await EmbedHandler.CreateBasicEmbed("Music, Join", $"Joined {voiceState.VoiceChannel.Name}.", Color.Green);
            }
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Join", ex.Message);
            }
        }

        /*This is ran when a user uses either the command Join or Play
            I decided to put these two commands as one, will probably change it in future. 
            Task Returns an Embed which is used in the command call.. */
        public async Task<Embed> PlayAsync(SocketGuildUser user, IGuild guild, string query)
        {
            //Check If User Is Connected To Voice Cahnnel.
            if (user.VoiceChannel == null)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Join/Play", "You Must First Join a Voice Channel.");
            }

            //Check the guild has a player available.
            if (!_lavaNode.HasPlayer(guild))
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Play", "I'm not connected to a voice channel.");
            }

            try
            {
                // Clear pending choice.
                _serverData[guild].PendingSelect = null;

                //Get the player for that guild.
                var player = _lavaNode.GetPlayer(guild);

                var search = Uri.IsWellFormedUriString(query, UriKind.Absolute) ?
                    await _lavaNode.SearchAsync(SearchType.Direct, query)
                    : await _lavaNode.SearchYouTubeAsync(query);

                Embed embed;

                switch (search.Status)
                {
                    //If we couldn't find anything, tell the user.
                    case SearchStatus.NoMatches:
                        return await EmbedHandler.CreateErrorEmbed("Music", $"I wasn't able to find anything for {query}.");
                    //Load a whole playlist.
                    case SearchStatus.PlaylistLoaded:
                    {
                        var playlist = search.Playlist;
                        await LoggingService.LogInformationAsync("Music", $"{playlist.Name} has been added to the queue");
                        foreach (var t in search.Tracks)
                        {
                            player.Queue.Enqueue(t);
                            await LoggingService.LogInformationAsync("Music", $"{t.Title} has been added to the music queue.");
                        }
                        embed = await EmbedHandler.CreateBasicEmbed("Music", $"{playlist.Name} has been added to the queue", Color.Blue);
                        break;
                    }
                    case SearchStatus.TrackLoaded:
                    {
                        var t = search.Tracks.First();
                        player.Queue.Enqueue(t);
                        await LoggingService.LogInformationAsync("Music", $"{t.Title} has been added to the music queue.");
                        embed = await EmbedHandler.CreateBasicEmbed("Music", $"{t.Title} has been added to queue.", Color.Blue);
                        break;
                    }
                    case SearchStatus.SearchResult:
                    {
                        var tracks = search.Tracks.Take(5);
                        _serverData[guild].PendingSelect = tracks;
                        return await EmbedHandler.CreateBasicEmbed("Music",
                            $"Please select your preferred track\n{string.Join('\n', tracks.Select((t, index) => $"{index + 1}. [{t.Title}]({t.Url})"))}",
                            Color.Blue);
                    }
                    case SearchStatus.LoadFailed:
                    default:
                    {
                        await LoggingService.LogCriticalAsync("Music", search.Exception.ToString());
                        return await EmbedHandler.CreateErrorEmbed("Music", "Cannot load music.");
                    }
                }

                //There's currently nothing to play. There should be a track to dequeue.
                if (player.Track == null || player.PlayerState == PlayerState.Stopped)
                {
                    LavaTrack track;
                    player.Queue.TryDequeue(out track);

                    //Player was not playing anything, so lets play the requested track.
                    await player.PlayAsync(track);
                    await LoggingService.LogInformationAsync("Music", $"Bot Now Playing: {track.Title}\nUrl: {track.Url}");
                    return await EmbedHandler.CreateBasicEmbed("Music", $"Now Playing: {track.Title}\nUrl: {track.Url}", Color.Blue);
                }

                return embed;
            }

            //If after all the checks we did, something still goes wrong. Tell the user about it so they can report it back to us.
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Play", ex.Message);
            }

        }

        /*This is ran when a user uses the command Leave.
            Task Returns an Embed which is used in the command call. */
        public async Task<Embed> LeaveAsync(IGuild guild)
        {
            try
            {
                //Get The Player Via GuildID.
                var player = _lavaNode.GetPlayer(guild);

                //if The Player is playing, Stop it.
                if (player.PlayerState is PlayerState.Playing)
                {
                    await player.StopAsync();
                }

                //Leave the voice channel.
                await _lavaNode.LeaveAsync(player.VoiceChannel);

                // Remove the server data.
                _serverData.Remove(guild);

                await LoggingService.LogInformationAsync("Music", $"Bot has left.");
                return await EmbedHandler.CreateBasicEmbed("Music", $"I've left. Thank you for playing moosik.", Color.Blue);
            }
            //Tell the user about the error so they can report it back to us.
            catch (InvalidOperationException ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Leave", ex.Message);
            }
        }

        /*This is ran when a user uses the command List 
            Task Returns an Embed which is used in the command call. */
        public async Task<Embed> ListAsync(IGuild guild, int? page)
        {
            const int tracksPerPage = 5;
            try
            {
                page ??= 1;
                --page;

                /* Create a string builder we can use to format how we want our list to be displayed. */
                var descriptionBuilder = new StringBuilder();

                /* Get The Player and make sure it isn't null. */
                var player = _lavaNode.GetPlayer(guild);
                if (player == null)
                    return await EmbedHandler.CreateErrorEmbed("Music, List", $"Could not aquire player.\nAre you using the bot right now? check{GlobalData.Config.DefaultPrefix}Help for info on how to use the bot.");

                if (player.PlayerState is PlayerState.Playing)
                {
                    /*If the queue count is less than 1 and the current track IS NOT null then we wont have a list to reply with.
                        In this situation we simply return an embed that displays the current track instead. */
                    if (player.Queue.Count < 1 && player.Track != null)
                    {
                        return await EmbedHandler.CreateBasicEmbed($"Now Playing: {player.Track.Title}", "Nothing Else Is Queued.", Color.Blue);
                    }
                    else
                    {
                        /* Now we know if we have something in the queue worth replying with, so we itterate through all the Tracks in the queue.
                         *  Next Add the Track title and the url however make use of Discords Markdown feature to display everything neatly.
                            This trackNum variable is used to display the number in which the song is in place. (Start at 2 because we're including the current song.*/
                        var trackNum = 2 + page * tracksPerPage;
                        foreach (LavaTrack track in player.Queue.Skip((int)page * tracksPerPage).Take(5))
                        {
                            descriptionBuilder.Append($"{trackNum}: [{track.Title}]({track.Url}) - {track.Id}\n");
                            trackNum++;
                        }
                        return await EmbedHandler.CreateBasicEmbed("Music Playlist", $"Now Playing: [{player.Track.Title}]({player.Track.Url}) \n{descriptionBuilder}", Color.Blue);
                    }
                }
                else
                {
                    return await EmbedHandler.CreateErrorEmbed("Music, List", "Player doesn't seem to be playing anything right now. If this is an error, Please Contact trungnt2910.");
                }
            }
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, List", ex.Message);
            }

        }

        /*This is ran when a user uses the command Skip 
            Task Returns an Embed which is used in the command call. */
        public async Task<Embed> SkipTrackAsync(IGuild guild)
        {
            try
            {
                var player = _lavaNode.GetPlayer(guild);
                /* Check if the player exists */
                if (player == null)
                    return await EmbedHandler.CreateErrorEmbed("Music, List", $"Could not aquire player.\nAre you using the bot right now? check{GlobalData.Config.DefaultPrefix}Help for info on how to use the bot.");
                /* Check The queue, if it is less than one (meaning we only have the current song available to skip) it wont allow the user to skip.
                     User is expected to use the Stop command if they're only wanting to skip the current song. */
                if (player.Queue.Count < 1)
                {
                    return await EmbedHandler.CreateErrorEmbed("Music, SkipTrack", $"Unable To skip a track as there is only One or No songs currently playing." +
                        $"\n\nDid you mean {GlobalData.Config.DefaultPrefix}Stop?");
                }
                else
                {
                    try
                    {
                        /* Save the current song for use after we skip it. */
                        var currentTrack = player.Track;
                        /* Skip the current song. */
                        await player.SkipAsync();
                        await LoggingService.LogInformationAsync("Music", $"Bot skipped: {currentTrack.Title}");
                        return await EmbedHandler.CreateBasicEmbed("Music Skip", $"I have successfully skiped {currentTrack.Title}", Color.Blue);
                    }
                    catch (Exception ex)
                    {
                        return await EmbedHandler.CreateErrorEmbed("Music, Skip", ex.Message);
                    }

                }
            }
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Skip", ex.Message);
            }
        }

        /*This is ran when a user uses the command Stop 
            Task Returns an Embed which is used in the command call. */
        public async Task<Embed> StopAsync(IGuild guild)
        {
            try
            {
                var player = _lavaNode.GetPlayer(guild);

                if (player == null)
                    return await EmbedHandler.CreateErrorEmbed("Music, List", $"Could not aquire player.\nAre you using the bot right now? check{GlobalData.Config.DefaultPrefix}Help for info on how to use the bot.");

                /* Check if the player exists, if it does, check if it is playing.
                     If it is playing, we can stop.*/
                if (player.PlayerState is PlayerState.Playing)
                {
                    await player.StopAsync();
                }

                await LoggingService.LogInformationAsync("Music", $"Bot has stopped playback.");
                return await EmbedHandler.CreateBasicEmbed("Music Stop", "I Have stopped playback & the playlist has been cleared.", Color.Blue);
            }
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Stop", ex.Message);
            }
        }

        /*This is ran when a user uses the command Volume 
            Task Returns a String which is used in the command call. */
        public async Task<string> SetVolumeAsync(IGuild guild, int volume)
        {
            if (volume > 150 || volume <= 0)
            {
                return $"Volume must be between 1 and 150.";
            }
            try
            {
                var player = _lavaNode.GetPlayer(guild);
                await player.UpdateVolumeAsync((ushort)volume);
                await LoggingService.LogInformationAsync("Music", $"Bot Volume set to: {volume}");
                return $"Volume has been set to {volume}.";
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
        }

        public async Task<string> PauseAsync(IGuild guild)
        {
            try
            {
                var player = _lavaNode.GetPlayer(guild);
                if (!(player.PlayerState is PlayerState.Playing))
                {
                    await player.PauseAsync();
                    return $"There is nothing to pause.";
                }

                await player.PauseAsync();
                return $"**Paused:** {player.Track.Title}, what a bamboozle.";
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
        }

        public async Task<string> ResumeAsync(IGuild guild)
        {
            try
            {
                var player = _lavaNode.GetPlayer(guild);

                if (player.PlayerState is PlayerState.Paused)
                {
                    await player.ResumeAsync();
                }

                return $"**Resumed:** {player.Track.Title}";
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
        }

        public async Task TrackEnded(TrackEndedEventArgs args)
        {
            if (args.Reason != TrackEndReason.Finished)
            {
                return;
            }

            if (!args.Player.Queue.TryDequeue(out var queueable))
            {
                //await args.Player.TextChannel.SendMessageAsync("Playback Finished.");
                return;
            }

            Console.WriteLine("Playing next track...");

            if (!(queueable is LavaTrack track))
            {
                await args.Player.TextChannel.SendMessageAsync("Next item in queue is not a track.");
                return;
            }

            await args.Player.PlayAsync(track);
            await args.Player.TextChannel.SendMessageAsync(
                embed: await EmbedHandler.CreateBasicEmbed("Now Playing", $"[{track.Title}]({track.Url})", Color.Blue));
        }

        public async Task<Embed> SelectAsync(IGuild guild, int index)
        {
            if (!_serverData.ContainsKey(guild) || _serverData[guild].PendingSelect == null)
            {
                return await EmbedHandler.CreateErrorEmbed("Music", "There are no open selection dialogs!");
            }

            var list = _serverData[guild].PendingSelect.ToList();
            _serverData[guild].PendingSelect = null;

            --index;
            if (index >= list.Count || index < 0)
            {
                return await EmbedHandler.CreateErrorEmbed("Music", $"Invalid index: {index + 1}");
            }

            var player = _lavaNode.GetPlayer(guild);
            var track = list[index];

            if (player.Track == null || player.PlayerState == PlayerState.Stopped)
            {
                //Player was not playing anything, so lets play the requested track.
                await player.PlayAsync(track);
                await LoggingService.LogInformationAsync("Music", $"Bot Now Playing: {track.Title}\nUrl: {track.Url}");
                return await EmbedHandler.CreateBasicEmbed("Music", $"Now Playing: {track.Title}\nUrl: {track.Url}", Color.Blue);
            }

            player.Queue.Enqueue(track);
            await LoggingService.LogInformationAsync("Music", $"{track.Title} has been added to the queue.");
            return await EmbedHandler.CreateBasicEmbed("Music", $"{track.Title} has been added to the queue.", Color.Blue);
        }

        public async Task<string> SetNightcoreAsync(IGuild guild, string enableString)
        {
            try
            {
                var enable = false;
                if (!string.IsNullOrWhiteSpace(enableString))
                {
                    enable = bool.Parse(enableString);
                }

                var player = _lavaNode.GetPlayer(guild);

                if (enable)
                {
                    await player.ApplyFilterAsync(new TimescaleFilter() { Pitch = 1.2999999523162842, Speed = 1.2999999523162842, Rate = 1 });
                    return $"`nightcore` effect applied.";
                }
                else
                {
                    await player.ApplyFilterAsync(new TimescaleFilter() { Pitch = 1, Speed = 1, Rate = 1 });
                    return $"`nightcore` effect removed.";
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<Embed> SetSpeedAsync(IGuild guild, double? speed)
        {
            try
            {
                if (!_serverData.ContainsKey(guild))
                {
                    return await EmbedHandler.CreateErrorEmbed("Music", "I'm not connected to a voice channel yet!");
                }

                var player = _lavaNode.GetPlayer(guild);
                var serverData = _serverData[guild];

                // annette-speed
                // return the current speed.
                if (speed == null)
                {
                    return await EmbedHandler.CreateBasicEmbed("Music", $"Current Speed: {serverData.Speed}", Color.Blue);
                }

                // set the speed.
                await player.ApplyFilterAsync(new TimescaleFilter() { Pitch = 1, Speed = speed.Value, Rate = 1 });
                serverData.Speed = speed.Value;
                return await EmbedHandler.CreateBasicEmbed("Music", $"Speed set to: {speed.Value}", Color.Blue);
            }
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music", $"Failed to set music speed: {ex.Message}");
            }
        }
    }
}
