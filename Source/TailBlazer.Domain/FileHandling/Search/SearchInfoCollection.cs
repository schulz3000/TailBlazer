﻿using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using TailBlazer.Domain.Annotations;

namespace TailBlazer.Domain.FileHandling.Search
{
    public sealed class SearchInfoCollection : ISearchInfoCollection
    {
        private readonly ISearchMetadataCollection _localMetadataCollection;
        private readonly ICombinedSearchMetadataCollection _combinedSearchMetadataCollection;
        private readonly ISearchMetadataFactory _searchMetadataFactory;
        private readonly IFileWatcher _fileWatcher;
        private readonly IDisposable _cleanUp;

        public IObservableCache<SearchInfo, string> Searches { get; }
        
        public IObservable<ILineProvider> All { get; }
        
        public SearchInfoCollection(ICombinedSearchMetadataCollection combinedSearchMetadataCollection,
            ISearchMetadataFactory searchMetadataFactory,
            IFileWatcher fileWatcher)
        {
            _localMetadataCollection = combinedSearchMetadataCollection.Local;
            _combinedSearchMetadataCollection = combinedSearchMetadataCollection;
            _searchMetadataFactory = searchMetadataFactory;
            _fileWatcher = fileWatcher;

            //Add a complete file display
            All = _fileWatcher.Latest.Index().Replay(1).RefCount();

            //create a collection with 1 item, which is used to show entire file
            var systemSearches = new SourceCache<SearchInfo, string>(t => t.SearchText);
            systemSearches.AddOrUpdate(new SearchInfo("<All>", false, All, SearchType.All));
            
            //create a collection of all possible user filters
            var userSearches = combinedSearchMetadataCollection.Combined
                .Connect(meta => meta.Filter)
                .IgnoreUpdateWhen((current,previous)=> SearchMetadata.EffectsFilterComparer.Equals(current, previous))
                .Transform(meta =>
                {
                    var latest = _fileWatcher.Latest
                        .Search(meta.BuildPredicate())
                        .Replay(1).RefCount();

                    return new SearchInfo(meta.SearchText, meta.IsGlobal, latest, SearchType.User);
                });

            //combine te results into a single collection
            Searches = systemSearches.Connect()
                    .Or(userSearches)
                    .AsObservableCache();

            _cleanUp = new CompositeDisposable(Searches, systemSearches);
        }

        public void Add([NotNull] string searchText, bool useRegex)
        {
            if (searchText == null) throw new ArgumentNullException(nameof(searchText));

            var index = _localMetadataCollection.NextIndex();
            var metatdata = _searchMetadataFactory.Create(searchText, useRegex, index,true);
            _localMetadataCollection.AddorUpdate(metatdata);
        }

        public void Remove(string searchText)
        {
            var item = Searches.Lookup(searchText);
            if (!item.HasValue) return;


            if (!item.Value.IsGlobal)
            {
                _localMetadataCollection.Remove(searchText);
            }
            else
            {
                _combinedSearchMetadataCollection.Global.Remove(searchText);
            }

        }

        public void Dispose()
        {
            _cleanUp.Dispose();
        }
    }
}
