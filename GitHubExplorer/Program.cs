using Newtonsoft.Json;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;

namespace GitHubExplorer
{
   class Program
   {
      
      static async Task Main(string[] args)
      {
         var github = CreateGitHubClient(args[0], args[1]);
         var rootFolder = args[2];
         var numberOfResponsesToParse = int.Parse(args[3]);
         var degreeOfParallelism = int.Parse(args[4]);

         var searchResult = await github.Search.SearchRepo(new SearchRepositoriesRequest()
         {
            Language = Language.CSharp,
            SortField = RepoSearchSort.Stars
         });


         var results = await AnalyzeRepos(searchResult.Items, rootFolder, numberOfResponsesToParse, degreeOfParallelism);
         Console.WriteLine($"Results: {JsonConvert.SerializeObject(results)}");
      }

      /// <summary>
      /// Analyzes the set of repos in parallel.
      /// Dumping them in the rootFolder, and then return
      /// the results of the analysis.
      /// </summary>
      /// <param name="repos">The set of repo we are going to analyze</param>
      /// <param name="rootFolder">the folder where we will dump the cloned github repo into</param>
      /// <param name="repositoriesToParse">the total number of repositories to parse.</param>
      /// <param name="degreeOfParallelism">The total number of repos to download at once. Use this number
      /// to control filling up disk space.</param>
      /// <returns>The results of the analysis.</returns>
      public static async Task<List<RepoAnalysisResults>> AnalyzeRepos(IEnumerable<Repository> repos, 
                                                                        string rootFolder, 
                                                                        int repositoriesToParse, 
                                                                        int degreeOfParallelism)
      {
         var reposToAnalyze = repos.Take(repositoriesToParse).ToList();
         var initialRepos = reposToAnalyze.Take(degreeOfParallelism).ToList();
         int nextRepoNumber = degreeOfParallelism;

         List<Task<RepoAnalysisResults>> repoAnalysisTasks = initialRepos.Select(r => AnalyzeRepo(r, rootFolder)).ToList();
         List<RepoAnalysisResults> results = new List<RepoAnalysisResults>();

         while (repoAnalysisTasks.Any())
         {
            Task<RepoAnalysisResults> finishedAnalysis = await Task.WhenAny(repoAnalysisTasks);

            repoAnalysisTasks.Remove(finishedAnalysis);

            var analysis = await finishedAnalysis;
            Console.WriteLine($"Finished Analysis: {analysis}");
            results.Add(analysis);

            if (nextRepoNumber < reposToAnalyze.Count)
            {
               var nextRepo = reposToAnalyze[nextRepoNumber];
               Console.WriteLine($"Adding Analysis For Repo: {nextRepo.Name}");
               repoAnalysisTasks.Add(AnalyzeRepo(nextRepo, rootFolder));
               nextRepoNumber++;
            }
         }

         return results;
      }

      /// <summary>
      /// Analyzes the given repo, that starts at the given folder.
      /// Creates a new folder within root folder to download the 
      /// git files into.
      /// </summary>
      /// <param name="repo">the repository we are analyzing.</param>
      /// <param name="rootFolder">the root folder to dump the results into</param>
      /// <returns>the results of the repo analysis.</returns>
      public static async Task<RepoAnalysisResults> AnalyzeRepo(Repository repo, string rootFolder)
      {        
            var folder = await CloneRepo(repo, rootFolder);

            int testLines = 0;
            int nonTestLines = 0;

            if (folder != null)
            {
               Console.WriteLine($"Getting Test Files For: {repo.Name}");
               var testFilesTask = GetTestFiles(folder);


               Console.WriteLine($"Getting Non Test Files For: {repo.Name}");
               var nonTestFilesTask = GetNonTestFiles(folder);

               await Task.WhenAll(testFilesTask, nonTestFilesTask);

               var testFiles = await testFilesTask;
               var nonTestFiles = await nonTestFilesTask;

               Console.WriteLine($"Counting Lines For: {repo.Name}");
               var testLinesTask = CountLines(testFiles);
               var nonTestLinesTask = CountLines(nonTestFiles);

               testLines = await testLinesTask;
               nonTestLines = await nonTestLinesTask;
               await Task.WhenAll(testLinesTask, nonTestLinesTask);
            }

            Console.WriteLine($"Deleting Temp Directory: {repo.Name}");
            DeleteDirectory(folder);

            return new RepoAnalysisResults(repo.Name, repo.Url, testLines, nonTestLines);
         
      }

      public static void DeleteDirectory(string target_dir)
      {
      
         string[] files = Directory.GetFiles(target_dir);
         string[] dirs = Directory.GetDirectories(target_dir);

         foreach (string file in files)
         {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
         }

         foreach (string dir in dirs)
         {
            DeleteDirectory(dir);
         }

         Directory.Delete(target_dir, false);
      }

      /// <summary>
      /// Gets the files that are marked as "Tests"
      /// </summary>
      /// <param name="folder">the folder we are starting to parse into.</param>
      /// <returns>the full paths of each file.</returns>
      public static Task<IEnumerable<string>> GetTestFiles(string folder)
      {
          return Task.Run(() => {
             return Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
                                   .Where(s => Path.GetDirectoryName(s.ToLowerInvariant()).Contains("test"));
          });
      }


      /// <summary>
      /// Gets the files that are not marked as tests.
      /// </summary>
      /// <param name="folder">the folder to start grabbing "NonTestFiles"</param>
      /// <returns>the complete paths of files that aren not tests.</returns>
      public static Task<IEnumerable<string>> GetNonTestFiles(string folder)
      {
         return Task.Run(() => {
            return Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
                                   .Where(s => !Path.GetDirectoryName(s.ToLowerInvariant()).Contains("test"));

                                   });
      }

      public static async Task<int> CountLines(IEnumerable<string> files)
      {
         int totalLines = 0;

         List<Task<string>> fileContentTasks = new List<Task<string>>();
         foreach (var file in files)
         {
            fileContentTasks.Add(File.ReadAllTextAsync(file));  
         }

         await Task.WhenAll(fileContentTasks);

         foreach(var fileContentTask in fileContentTasks)
         {
            var fileContent = await fileContentTask;
            totalLines += fileContent.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;
         }

         return totalLines;
      }

     
      static async Task<string> CloneRepo(Repository cloneRepo, string rootFolder)
      {
         try
         {
            InitialSessionState initial = InitialSessionState.CreateDefault();
            initial.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Unrestricted;
            Runspace runspace = RunspaceFactory.CreateRunspace(initial);
            runspace.Open();

            var ps = PowerShell.Create();
            ps.Runspace = runspace;

            Console.WriteLine($"Cloning Git: {cloneRepo.CloneUrl}");
            ps.AddCommand(@".\CloneGit_CreateFolder.ps1");
            ps.AddParameter("clone_url", cloneRepo.CloneUrl);
            ps.AddParameter("root_folder", rootFolder);
            var results = await ps.InvokeAsync();

            Console.WriteLine($" FinishedCloning Git: {cloneRepo.CloneUrl}");
            if (results.Count > 0)
               return results[0].ToString();
            else
               return null;
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Excpetion When Cloning: {cloneRepo.CloneUrl} Message:{ex.Message}, StackTrace: {ex.StackTrace}");
            return null;
         }

      }


      static GitHubClient CreateGitHubClient(string username, string personalAccessToken)
      {
         var productHeader = new ProductHeaderValue(username);
         var github = new GitHubClient(productHeader);
         github.Credentials = new Credentials(personalAccessToken);
         return github;
      }

    
   }
}
