using System;
using System.IO;
using DiscuitSharp.Core;
using System.Net;
using DiscuitSharp.Core.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using DiscuitSharp.Core.Auth;
using Microsoft.VisualBasic;

var builder = new ConfigurationBuilder()
                .AddJsonFile("local.settings.json", optional: true);
IConfiguration config = builder.Build();

var Client = new HttpClient(new HttpClientHandler() { CookieContainer = new CookieContainer() })
{
    BaseAddress = new Uri("https://discuit.net/api/")
};
CancellationTokenSource cts = new CancellationTokenSource();
var token = cts.Token;

var creds = new Credentials(config["UserName"]?.ToString() ?? string.Empty, config["Password"]?.ToString() ?? string.Empty);
var client = new DiscuitClient(Client);
_ = await client.GetInitial();
var beetlejuice = await client.Authenticate(creds);

DateTime lastActive = DateTime.UtcNow.Subtract(new TimeSpan(0,10,0));


var timer = new PeriodicTimer(new TimeSpan(0,5,0));
do {
    var postCursor = await client.GetPosts(feed: Feed.All, sort:Sort.Activity);
    if(postCursor == null) continue;
    if(postCursor.Records == null) continue;
    bool done = false;

    List<Post> posts = new (postCursor.Records.Where(p => p.LastActivityAt > lastActive));
    if(posts.Count < postCursor.Records.Count) {
        done = true;
    }
    while (!done && postCursor?.Next != null)
    {
        postCursor = await client.GetPosts(new CursorIndex(postCursor.Next), feed: Feed.All, sort: Sort.Activity);
        if (postCursor == null || postCursor.Records == null){
            done = true;
            continue;
        }
        var add = postCursor.Records.Where(p => p.LastActivityAt > lastActive);
        if(add == null || add.Count() < postCursor.Records.Count)
        {
            done = true;
        }
        posts.AddRange(add);
    }

    foreach(var post in posts){ 
        var parentIds = new List<CommentId>();
        var commentCursor = await client.GetComments(post.PublicId!.Value);
        if (commentCursor == null) continue;
        if (commentCursor.Records == null) continue;

        HashSet<CommentId> beetleSays = commentCursor.Records.Where(c => c!= null &&  c.UserId == beetlejuice.Id.ToString())
            .Where(c => c.ParentId != null)
            .Select(c => c.ParentId.Value)
            .ToHashSet();

        Dictionary<CommentId, Comment?> hash = commentCursor.Records.Where(c => c!= null && c.Id != null).ToDictionary(p => p.Id!.Value, p => p);

        var beetlejuices = commentCursor?.Records?.Where(c => c.Body.IndexOf("beetlejuice", StringComparison.OrdinalIgnoreCase) >= 0);
        foreach(var b in beetlejuices)
        {
            if(search_rec(hash, beetleSays, b, 0) is CommentId parentId)
                parentIds.Add(b.Id!.Value);
        }
        foreach(var parent in parentIds)
        {
            Comment ImHere = new( config["intro"]!.ToString());
            _ = await client.Create(post.PublicId!.Value, parent, ImHere);
            
        }
    }
    lastActive = DateTime.UtcNow;
}
while(await timer.WaitForNextTickAsync(token));


CommentId? search_rec(Dictionary<CommentId, Comment> posts, HashSet<CommentId> exclusions, Comment comment, int count)
{
    if(exclusions.Contains(comment.Id.Value)) return null;
    foreach(var str in comment.Body.Split(' ', ',', '!'))
        count += string.Equals(str.Trim(), "beetlejuice", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    if(comment.ParentId == null)
        return (count == 3)? comment.Id : null;

    if (count == 3)
        return comment.Id;
    else
        return search_rec(posts, exclusions, posts[comment.ParentId!.Value], count+1);
    
}
