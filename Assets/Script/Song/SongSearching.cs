using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YARG.Core;
using YARG.Core.Song;
using UnityEngine;

namespace YARG.Song
{
    public class SongSearching
    {
        public IReadOnlyList<SongCategory> Refresh(SongAttribute sort)
        {
            searches.Clear();
            var filter = new FilterNode(sort, string.Empty);
            var songs = SongContainer.GetSortedSongList(sort);
            searches.Add(new SearchNode(filter, songs));
            return songs;
        }

        public IReadOnlyList<SongCategory> Search(string value, SongAttribute sort)
        {
            var currentFilters = new List<FilterNode>()
            {
                new(sort, string.Empty)
            };
            currentFilters.AddRange(GetFilters(value.Split(';')));

            for (int i = 1; i < currentFilters.Count; i++)
            {
                if (currentFilters[i].attribute == SongAttribute.Instrument)
                {
                    currentFilters[0] = currentFilters[i];
                    currentFilters.RemoveAt(i);
                    break;
                }
            }

            int currFilterIndex = 0;
            int prevFilterIndex = 0;
            while (currFilterIndex < currentFilters.Count && prevFilterIndex < searches.Count)
            {
                while (currentFilters[currFilterIndex].StartsWith(searches[prevFilterIndex].Filter))
                {
                    ++prevFilterIndex;
                    if (prevFilterIndex == searches.Count)
                    {
                        break;
                    }
                }

                if (prevFilterIndex == 0 || currentFilters[currFilterIndex] != searches[prevFilterIndex - 1].Filter)
                {
                    break;
                }
                ++currFilterIndex;
            }

            // Apply new sort
            if (currFilterIndex == 0)
            {
                searches.Clear();
                var filter = currentFilters[0];
                var songs = SongContainer.GetSortedSongList(filter.attribute);
                if (filter.attribute == SongAttribute.Instrument)
                {
                    songs = FilterInstruments(songs, filter.argument);
                }
                searches.Add(new SearchNode(filter, songs));
                prevFilterIndex = 1;
                currFilterIndex = 1;
            }

            while (currFilterIndex < currentFilters.Count)
            {
                var filter = currentFilters[currFilterIndex];
                var searchList = SearchSongs(filter, searches[prevFilterIndex - 1].Songs);

                if (prevFilterIndex < searches.Count)
                {
                    searches[prevFilterIndex] = new(filter, searchList);
                }
                else
                {
                    searches.Add(new(filter, searchList));
                }

                ++currFilterIndex;
                ++prevFilterIndex;
            }

            if (prevFilterIndex < searches.Count)
            {
                searches.RemoveRange(prevFilterIndex, searches.Count - prevFilterIndex);
            }
            return searches[prevFilterIndex - 1].Songs;
        }

        public bool IsUnspecified()
        {
            if (searches.Count <= 0)
            {
                return true;
            }

            return searches[^1].Filter.attribute == SongAttribute.Unspecified;
        }

        private class FilterNode : IEquatable<FilterNode>
        {
            public readonly SongAttribute attribute;
            public readonly string argument;

            public FilterNode(SongAttribute attribute, string argument)
            {
                this.attribute = attribute;
                this.argument = argument;
            }

            public override bool Equals(object o)
            {
                return o is FilterNode node && Equals(node);
            }

            public bool Equals(FilterNode other)
            {
                return attribute == other.attribute && argument == other.argument;
            }

            public override int GetHashCode()
            {
                return attribute.GetHashCode() ^ argument.GetHashCode();
            }

            public static bool operator ==(FilterNode left, FilterNode right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(FilterNode left, FilterNode right)
            {
                return !left.Equals(right);
            }

            public bool StartsWith(FilterNode other)
            {
                return attribute == other.attribute && argument.StartsWith(other.argument);
            }
        }

        private class SearchNode
        {
            public readonly FilterNode Filter;
            public IReadOnlyList<SongCategory> Songs;

            public SearchNode(FilterNode filter, IReadOnlyList<SongCategory> songs)
            {
                Filter = filter;
                Songs = songs;
            }
        }

        private List<SearchNode> searches = new();

        private static List<FilterNode> GetFilters(string[] split)
        {
            var nodes = new List<FilterNode>();
            foreach (string arg in split)
            {
                SongAttribute attribute;
                string argument = arg.Trim();
                if (argument == string.Empty)
                {
                    continue;
                }

                if (argument.StartsWith("artist:"))
                {
                    attribute = SongAttribute.Artist;
                    argument = RemoveDiacriticsAndArticle(argument[7..]);
                }
                else if (argument.StartsWith("source:"))
                {
                    attribute = SongAttribute.Source;
                    argument = argument[7..].ToLower();
                }
                else if (argument.StartsWith("album:"))
                {
                    attribute = SongAttribute.Album;
                    argument = SortString.RemoveDiacritics(argument[6..]);
                }
                else if (argument.StartsWith("charter:"))
                {
                    attribute = SongAttribute.Charter;
                    argument = argument[8..].ToLower();
                }
                else if (argument.StartsWith("year:"))
                {
                    attribute = SongAttribute.Year;
                    argument = argument[5..].ToLower();
                }
                else if (argument.StartsWith("genre:"))
                {
                    attribute = SongAttribute.Genre;
                    argument = argument[6..].ToLower();
                }
                else if (argument.StartsWith("playlist:"))
                {
                    attribute = SongAttribute.Playlist;
                    argument = argument[9..].ToLower();
                }
                else if (argument.StartsWith("name:"))
                {
                    attribute = SongAttribute.Name;
                    argument = RemoveDiacriticsAndArticle(argument[5..]);
                }
                else if (argument.StartsWith("title:"))
                {
                    attribute = SongAttribute.Name;
                    argument = RemoveDiacriticsAndArticle(argument[6..]);
                }
                else if (argument.StartsWith("instrument:"))
                {
                    attribute = SongAttribute.Instrument;
                    argument = RemoveDiacriticsAndArticle(argument[11..]);
                }
                else
                {
                    attribute = SongAttribute.Unspecified;
                    argument = SortString.RemoveDiacritics(argument);
                }

                argument = argument!.Trim();
                nodes.Add(new(attribute, argument));
                if (attribute == SongAttribute.Unspecified)
                {
                    break;
                }
            }
            return nodes;
        }

        private static List<SongCategory> SearchSongs(FilterNode arg, IReadOnlyList<SongCategory> searchList)
        {
            if (arg.attribute == SongAttribute.Unspecified)
            {
                List<SongEntry> entriesToSearch = new();
                foreach (var entry in searchList)
                {
                    entriesToSearch.AddRange(entry.Songs);
                }
                return UnspecifiedSearch(entriesToSearch, arg.argument);
            }

            if (arg.attribute == SongAttribute.Instrument)
            {
                return FilterInstruments(searchList, arg.argument);
            }

            Predicate<SongEntry> match = arg.attribute switch
            {
                SongAttribute.Name => entry => RemoveArticle(entry.Name.SortStr).Contains(arg.argument),
                SongAttribute.Artist => entry => RemoveArticle(entry.Artist.SortStr).Contains(arg.argument),
                SongAttribute.Album => entry => entry.Album.SortStr.Contains(arg.argument),
                SongAttribute.Genre => entry => entry.Genre.SortStr.Contains(arg.argument),
                SongAttribute.Year => entry => entry.Year.Contains(arg.argument) || entry.UnmodifiedYear.Contains(arg.argument),
                SongAttribute.Charter => entry => entry.Charter.SortStr.Contains(arg.argument),
                SongAttribute.Playlist => entry => entry.Playlist.SortStr.Contains(arg.argument),
                SongAttribute.Source => entry => entry.Source.SortStr.Contains(arg.argument),
                _ => throw new Exception("Unhandled seacrh filter")
            };

            List<SongCategory> result = new();
            foreach (var node in searchList)
            {
                var entries = node.Songs.FindAll(match);
                if (entries.Count > 0)
                {
                    result.Add(new SongCategory(node.Category, entries));
                }
            }
            return result;
        }

        private class UnspecifiedSortNode : IComparable<UnspecifiedSortNode>
        {
            public readonly SongEntry Song;
            public readonly int Rank;

            private readonly int NameIndex;
            private readonly int ArtistIndex;

            public UnspecifiedSortNode(SongEntry song, string argument)
            {
                Song = song;
                NameIndex = song.Name.SortStr.IndexOf(argument, StringComparison.Ordinal);
                ArtistIndex = song.Artist.SortStr.IndexOf(argument, StringComparison.Ordinal);

                Rank = NameIndex;
                if (Rank < 0 || (ArtistIndex >= 0 && ArtistIndex < Rank))
                {
                    Rank = ArtistIndex;
                }
            }

            public int CompareTo(UnspecifiedSortNode other)
            {
                if (Rank != other.Rank)
                {
                    return Rank - other.Rank;
                }

                if (NameIndex >= 0)
                {
                    if (other.NameIndex < 0)
                    {
                        // Prefer Name to Artist for equality
                        // other.ArtistIndex guaranteed valid
                        return NameIndex <= other.ArtistIndex ? -1 : 1;
                    }

                    if (NameIndex != other.NameIndex)
                    {
                        return NameIndex - other.NameIndex;
                    }
                    return Song.Name.CompareTo(other.Song.Name);
                }

                // this.ArtistIndex guaranteed valid from this point

                if (other.NameIndex >= 0)
                {
                    return ArtistIndex < other.NameIndex ? -1 : 1;
                }

                // other.ArtistIndex guaranteed valid from this point

                if (ArtistIndex != other.ArtistIndex)
                {
                    return ArtistIndex - other.ArtistIndex;
                }
                return Song.Artist.CompareTo(other.Song.Artist);
            }
        }

        private static List<SongCategory> UnspecifiedSearch(IReadOnlyList<SongEntry> songs, string argument)
        {
            var nodes = new UnspecifiedSortNode[songs.Count];
            Parallel.For(0, songs.Count, i => nodes[i] = new UnspecifiedSortNode(songs[i], argument));
            var results = nodes
                .Where(node => node.Rank >= 0)
                .OrderBy(i => i)
                .Select(i => i.Song).ToList();
            return new() { new SongCategory("Search Results", results) };
        }

        private static List<SongCategory> FilterInstruments(IReadOnlyList<SongCategory> searchList, string argument)
        {
                if(false){
            var instruments = ((Instrument[]) Enum.GetValues(typeof(Instrument)))
                .Select(ins => ins.ToString())
                .Where(str => {
                        foreach (var arg in argument.Split(",")) { 
                            // return true if the instrument string matches one of the arguments
                            if(str.Contains(arg, StringComparison.OrdinalIgnoreCase)){
                                return true;
                            }
                        }
                        // return false if no matches
                        return false;
                    })
                .ToArray();

            List<SongCategory> result = new();
            foreach (var node in searchList)
            {
                if (instruments.Contains(node.Category))
                {
                    result.Add(node);
                }
            }
            
            // Calculate intersection of songs in each node group, where a node group is what argument.Split(",") it matches with
            // Then update node.Category to contain all of the categories that intersect

            // FIXME:
            // Do something better than songEntry.ToString()
            Dictionary<string, uint> songCount = new();
            int totalCategoryCount = result.Count;

            // calculate the total count of each song in each node
            // if the final count of a song == number of nodes/categories, then it intersects all categories

            // FIXME:
            // NOT ALL CATEGORIES IN RESULT,
            // ONLY PER SEARCH ITEM CATEGORIES
            // Take union set of each search item category,
            // then iterate over that
            // maybe move argument.Split to have a greater scope?
            foreach (var node in result){
                foreach (var song in node.Songs){
                    uint count = 1;
                    // If the song hash already exists in the dictionary,
                    // then add 1 to the count,
                    // otherwise, set the count of that hash to 1
                    if(songCount.TryGetValue(song.ToString(), out count)){
                        ++count;
                    } 
                    songCount[song.ToString()] = count;
                }
            }

            List<SongEntry> finalEntries = new();
            foreach (SongEntry song in result[0].Songs) {
                if(songCount[song.ToString()] == totalCategoryCount) {
                    finalEntries.Add(song);
                } 
            }

            string finalCategory = String.Join(" ", instruments);

            SongCategory finalResult = new SongCategory(finalCategory, finalEntries);

            List<SongCategory> finalCategories = new();
            finalCategories.Add(finalResult);

            return finalCategories;
                } else {
                    return null;

/*
            // ALTERNATIVE:
            // Iterate over each song entry in each category,
            // check if it passes HasInstrument for each instrument
            // if it does, then add it to the final result
            string[][] instrumentGroups = new();
            string[] instrumentEnum = Enum.GetValues(typeof(Instrument)).Select(ins => ins.ToString()).ToArray();
            foreach (var arg in argument.Split(",")) { 
                string[] instruments = new();
                foreach(string insString in instrumentEnum) {
                    if(insString.Contains(arg, StringComparison.OrdinalIgnoreCase)){
                        instruments.Add(insString);
                    }
                }
                instrumentGroups.Add(instruments);
                List<SongEntry> argEntries = new();
                foreach (var node in searchList) {
                    if(instruments.Contains(node.Category)){
                        argEntries.Add(node.Songs);
                    }
                }
            }



                    List<SongEntry> finalEntries = new();
                    foreach (var node in searchList) {
                        if(instruments.Contains(node.Category)){
                        foreach(SongEntry song in node.Songs) {
                            hasAll = true;
                            foreach(string instrumentStr in instruments){
                                Enum.TryParse(instrumentStr, out Instrument ins);
                                // FIXME:
                                // Group by argument.Split(",")
                                if(!song.HasInstrument(ins)){
                                    hasAll = false;
                                }
                            }
                            if(hasAll){
                                finalEntries.Add(song);
                            }
                        }
                    }
                    SongCategory finalCategory = new SongCategory(argument, finalEntries);
                    List<SongCategory> finalList = new();
                    finalList.Add(finalCategory);
                    return finalCategory;
            }
            */
                }
        }

        private static readonly string[] Articles =
        {
            "The ", // The beatles, The day that never comes
            "El ",  // El final, El sol no regresa
            "La ",  // La quinta estacion, La bamba, La muralla verde
            "Le ",  // Le temps de la rentrée
            "Les ", // Les Rita Mitsouko, Les Wampas
            "Los ", // Los fabulosos cadillacs, Los enanitos verdes,
        };

        public static string RemoveArticle(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            foreach (var article in Articles)
            {
                if (name.StartsWith(article, StringComparison.InvariantCultureIgnoreCase))
                {
                    return name[article.Length..];
                }
            }

            return name;
        }

        public static string RemoveDiacriticsAndArticle(string text)
        {
            var textWithoutDiacritics = SortString.RemoveDiacritics(text);
            return RemoveArticle(textWithoutDiacritics);
        }
    }
}