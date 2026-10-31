using System;
using System.Collections.Generic;

namespace FlatSpace.AI
{
    /// <summary>
    /// One thing in a deal. kind: "planet" (name = planet), "fleet" (planet, name = ship kind name, count), "tech" (name),
    /// "endWar" (nothing else).
    /// </summary>
    [Serializable]
    public class DealItem
    {
        public string kind;
        public string name;
        public string planet;
        public int count;
    }

    /// <summary>A proposal: Give is what the proposer gives, Get is what it asks for.</summary>
    public class Deal
    {
        public List<DealItem> Give = new List<DealItem>();
        public List<DealItem> Get = new List<DealItem>();
    }

    /// <summary>How likely the rival is to accept, from the acceptance meter.</summary>
    public enum DealBand { VeryUnlikely, Unlikely, Uncertain, Likely, VeryLikely }

    /// <summary>One line of the reasons shown with a verdict; Helps = it pushes toward acceptance.</summary>
    public class DealFactor
    {
        public string Text;
        public bool Helps;
    }

    /// <summary>The rival's evaluation of a deal. Refusal is a closed reason when the deal is invalid, else null.</summary>
    public class DealVerdict
    {
        public float Receive, Give, PeaceWorth, Net, TangibleDelta;
        public DealBand Band;
        public bool Accept;
        public string Refusal;
        public List<DealFactor> Factors = new List<DealFactor>();
    }
}
