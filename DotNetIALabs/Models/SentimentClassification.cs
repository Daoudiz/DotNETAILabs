using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Models
{
   public enum Sentiment
    {
        Positive,
        Neutral,
        Negative
    }

    public sealed record SentimentClassification(
    Sentiment Sentiment,
    string Explanation);
}
