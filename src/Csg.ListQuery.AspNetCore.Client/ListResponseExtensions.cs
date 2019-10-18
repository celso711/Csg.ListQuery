﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Csg.ListQuery.AspNetCore.Abstractions;

namespace Csg.ListQuery.AspNetCore.Client
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public static class ListResponseExtensions
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    {
        /// <summary>
        /// Gets all data from a given source by making multiple API calls for each page using HTTP GET requests until the server indicates there are no more pages.
        /// </summary>
        /// <typeparam name="TData"></typeparam>
        /// <param name="client">The client to use for making requests.</param>
        /// <param name="response">The response object from the request for the first page</param>
        /// <param name="delayBetweenRequests">The delay (in milliseconds) to wait between requests.</param>
        /// <param name="maxRequests">The maximum number of requests to send to the source API.</param>
        /// <returns></returns>
        public static async Task<AggregateListResponse<TData>> GetAllPagesAsync<TData>(this IPagedListSupport client, IPagedListHateaosResponse<TData> response, int delayBetweenRequests = 25, int? maxRequests = null)
        {
            var result = response.Data;
            int pageCount = 1;
            int totalCount = response.Meta?.CurrentCount ?? 0;
            string nextUrl = response.Links?.Next;

            while (nextUrl != null && pageCount < (maxRequests ?? Int32.MaxValue))
            {
                await Task.Delay(delayBetweenRequests);
                var responseObject = await client.GetAsync<TData>(nextUrl);

                result = result.Concat(responseObject.Data);
                nextUrl = responseObject.Links?.Next;
                pageCount++;
                totalCount += responseObject.Meta?.CurrentCount ?? 0;
            }

            return new AggregateListResponse<TData>(result, totalCount, pageCount);
        }

        /// <summary>
        /// Gets all data from a given source by making multiple API calls for each page until the server indicates there are no more pages.
        /// </summary>
        /// <typeparam name="TData"></typeparam>
        /// <param name="client">The client to use for making requests.</param>
        /// <param name="request">The list request definition</param>
        /// <param name="delayBetweenRequests">The delay (in milliseconds) to wait between requests.</param>
        /// <param name="maxPagesToFetch">The maximum number of pages to fetch from the API.</param>
        /// <returns></returns>
        public static async Task<AggregateListResponse<TData>> PostAllPagesAsync<TData>(this IPagedListSupport client, IPagedListRequest request, int delayBetweenRequests = 25, int? maxPagesToFetch = null)
        {
            IEnumerable<TData> result = new List<TData>();
            int pageCount = 0;
            int totalCount = 0;
            int? nextOffset = request.Offset;

            while (nextOffset != null && pageCount < (maxPagesToFetch ?? Int32.MaxValue))
            {
                await Task.Delay(delayBetweenRequests);
                request.Offset = nextOffset.Value;
                var responseObject = await client.PostAsync<TData>(request);

                result = result.Concat(responseObject.Data);
                nextOffset = responseObject.Meta?.Next?.Offset;
                pageCount++;
                totalCount += responseObject.Meta?.CurrentCount ?? 0;
            }

            return new AggregateListResponse<TData>(result, totalCount, pageCount);
        }
    }
}
