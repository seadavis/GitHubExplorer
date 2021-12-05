using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitHubExplorer
{
   public class RepoAnalysisResults
   {
    
      public string RepoName { get; }

      public string CloneUrl { get; }

      public int NumberOfTestLines { get; }

      public int NumberOfNonTestLines { get;  }

      public RepoAnalysisResults(string repoName, string cloneUrl, int numberOfTestLines, int numberOfNonTestLines)
      {
         RepoName = repoName;
         CloneUrl = cloneUrl;
         NumberOfTestLines = numberOfTestLines;
         NumberOfNonTestLines = numberOfNonTestLines;
      }

      public override string ToString()
      {
         return JsonConvert.SerializeObject(this);
      }

   }
}
