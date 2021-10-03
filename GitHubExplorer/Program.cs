using Newtonsoft.Json;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GitHubExplorer
{
   class Program
   {
   
      static async Task Main(string[] args)
      {
         var github = CreateGitHubClient(args[0], args[1]);
         var searchResult = await github.Search.SearchRepo(new SearchRepositoriesRequest()
         {
            User = "AutoMapper",
            Language = Language.CSharp,
         });

         List<RepositoryContent> testDirectories = new List<RepositoryContent>();
         int testLines = 0;

         for (int i = 0; i < 1; i++)
         {

            var repo = searchResult.Items[i];
            var contents = await github.Repository.Content.GetAllContents(repo.Id);
            var foundTestDirectories = GetTestDirectories(contents, repo, github);

            await foreach(var testDirectory in foundTestDirectories)
            {
               testLines += await CountLines(github, repo, testDirectory, ".cs");
            }
         }
      
         Console.WriteLine($"Found: {testLines} lines of Test Code");

      }

     

      /// <summary>
      /// Counts the number of lines, with some
      /// content in them starting at the given directory and 
      /// recursing our way down.
      /// 
      /// Only searches files with the given file extension.
      /// </summary>
      /// <param name="dir">the directory we are looking for.</param>
      /// <param name="fileExtension">The name of the file extension we want to search</param>
      /// <param name="repo">the repo that owns the given directory</param>
      /// <param name="client">the client that is used to search the given repo.</param>
      /// <returns></returns>
      static async Task<int> CountLines(GitHubClient client, Repository repo, RepositoryContent dir, string fileExtension)
      {
         int total = 0;

         var subcontents = await client.Repository.Content.GetAllContents(repo.Id, dir.Path);
         Console.WriteLine($"Counting Directory: {dir.Name}");
         for(int i = 0; i < subcontents.Count; i++)
         {
            var content = subcontents[i];

            if(content.Type.Value == ContentType.Dir)
            {
               total += await CountLines(client, repo, content, fileExtension);
            }
            else if(content.Type.Value == ContentType.File)
            {
               var extension = Path.GetExtension(content.Name);

               if(extension == fileExtension)
               {
                  string fileContent = string.Empty;
                  using (WebClient webClient = new WebClient())
                  {
                     fileContent = webClient.DownloadString(content.DownloadUrl);
                  }
                  if (fileContent != null)
                  {
                     var numberOfLines = fileContent.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                                              .Length;
                     total += numberOfLines;
                  }

               }
            }
         }

         return total;

      }

      static GitHubClient CreateGitHubClient(string username, string personalAccessToken)
      {
         var productHeader = new ProductHeaderValue(username);
         var github = new GitHubClient(productHeader);
         github.Credentials = new Credentials(personalAccessToken);
         return github;
      }

      static async IAsyncEnumerable<RepositoryContent> GetTestDirectories(IReadOnlyList<RepositoryContent> contents, Repository repo, GitHubClient client)
      {

         for (int i = 0; i < contents.Count; i++)
         {
            if (contents[i].Type.Value == ContentType.Dir)
            {

               var subcontents = await client.Repository.Content.GetAllContents(repo.Id, contents[i].Path);
               var directoryName = contents[i].Name.ToLowerInvariant().Trim();
               var isTestDirectory = directoryName.Contains("test");

               if (!isTestDirectory)
               {
                  var innerContent = GetTestDirectories(subcontents, repo, client);

                  await foreach (var content in innerContent)
                  {
                     yield return content;
                  }
               }
               else
               {
                  yield return contents[i];
               }

            }

         }

      }
   }
}
