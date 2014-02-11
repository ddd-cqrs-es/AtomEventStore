﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grean.AtomEventStore
{
    public class AtomEventObserver<T>
    {
        private readonly UuidIri id;
        private readonly int pageSize;
        private readonly IAtomEventStorage storage;
        private readonly IContentSerializer serializer;

        public AtomEventObserver(
            UuidIri id,
            int pageSize,
            IAtomEventStorage storage,
            IContentSerializer serializer)
        {
            if (storage == null)
                throw new ArgumentNullException("storage");
            if (serializer == null)
                throw new ArgumentNullException("serializer");

            this.id = id;
            this.pageSize = pageSize;
            this.storage = storage;
            this.serializer = serializer;
        }

        public Task AppendAsync(T @event)
        {
            return Task.Factory.StartNew(() =>
            {
                var now = DateTimeOffset.Now;

                var index = this.ReadIndex();
                var firstLink = index.Links
                    .Where(l => l.IsFirstLink)
                    .DefaultIfEmpty(AtomLink.CreateFirstLink(new Uri(Guid.NewGuid().ToString(), UriKind.Relative)))
                    .Single();
                index = index.WithLinks(index.Links.Union(new[] { firstLink }));

                UuidIri firstId = Guid.Parse(firstLink.Href.ToString());

                var entry = CreateEntry(@event, now);

                var firstPage = this.ReadPage(firstLink.Href);

                if (firstPage.Entries.Count() >= this.pageSize)
                {
                    var nextId = UuidIri.NewId();
                    var nextAddress = new Uri(((Guid)nextId).ToString(), UriKind.Relative);
                    var nextPage = this.ReadPage(nextAddress);
                    nextPage = AddEntryTo(nextId, nextPage, entry, now);

                    var nextLink = AtomLink.CreateNextLink(nextAddress);
                    firstPage = firstPage
                        .WithLinks(firstPage.Links.Concat(new[] { nextLink }));

                    this.Write(index);
                    this.Write(firstPage);
                    this.Write(nextPage);
                }
                else
                {
                    firstPage = AddEntryTo(firstId, firstPage, entry, now);
                    index = index.WithLinks(
                        index.Links.Union(new[] { firstLink.ToLastLink() }));

                    this.Write(index);
                    this.Write(firstPage);
                }
            });
        }

        private AtomFeed ReadIndex()
        {
            var indexAddress =
                new Uri(((Guid)this.id).ToString(), UriKind.Relative);
            return ReadPage(indexAddress);
        }

        private AtomFeed ReadPage(Uri address)
        {
            using (var r = this.storage.CreateFeedReaderFor(address))
                return AtomFeed.ReadFrom(r, this.serializer);
        }

        private static AtomEntry CreateEntry(T @event, DateTimeOffset now)
        {
            var changesetId = Guid.NewGuid();
            return new AtomEntry(
                changesetId,
                "Changeset " + changesetId,
                now,
                now,
                new AtomAuthor("Grean"),
                new XmlAtomContent(@event),
                new AtomLink[0]);
        }

        private static AtomFeed AddEntryTo(
            UuidIri id,
            AtomFeed page,
            AtomEntry entry,
            DateTimeOffset now)
        {
            var entries = new[] { entry }.Concat(page.Entries);
            return CreateNewPage(id, entries, page.Links, now);
        }

        private static AtomFeed CreateNewPage(
            UuidIri id,
            IEnumerable<AtomEntry> entries,
            IEnumerable<AtomLink> links,
            DateTimeOffset now)
        {
            return new AtomFeed(
                id,
                "Partial event stream",
                now,
                new AtomAuthor("Grean"),
                entries,
                links);
        }

        private void Write(AtomFeed feed)
        {
            using (var w = this.storage.CreateFeedWriterFor(feed))
                feed.WriteTo(w, this.serializer);
        }

        public UuidIri Id
        {
            get { return this.id; }
        }

        public int PageSize
        {
            get { return this.pageSize; }
        }

        public IAtomEventStorage Storage
        {
            get { return this.storage; }
        }

        public IContentSerializer Serializer
        {
            get { return this.serializer; }
        }
    }
}