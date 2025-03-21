//  Copyright The LANDIS-II Foundation
//  Authors:  Robert M. Scheller, Brian Miranda, James B. Domingo

using Landis.SpatialModeling;
using System.Collections.Generic;


namespace Landis.Extension.ClimateBDA
{
    public class Epicenters
    {

        //---- NewEpicenters ------------------------------------------
        ///<summary>
        ///First, determine if new epicenter list required.
        ///If so, create the list.
        ///If not, create epicenter list from OUTSIDE of previous outbreak zones (only
        ///if User specified SeedEpicenter)
        ///If not, create epicenter list from INSIDE previous outbreak zones.
        ///The final epicenter list then spreads to neighboring areas of the landscape -
        ///see SpreadEpicenters()
        ///</summary>
        //---- NewEpicenters ------------------------------------------
        public static void NewEpicenters(IAgent agent, int BDAtimestep)
        {

            PlugIn.ModelCore.UI.WriteLine("   Creating New BDA Epicenters.");

            int numRows = (int) PlugIn.ModelCore.Landscape.Rows;
            int numCols = (int) PlugIn.ModelCore.Landscape.Columns;
            int epicenterNum = 0;
            int numInside = 0;
            int numOutside = 0;

            bool firstIteration = true;
            int oldEpicenterNum = agent.EpicenterNum;
            List<Location> newSiteList = new List<Location>(0);

            //Is the landscape empty of previous outbreak events?
            //If so, then generate a new list of epicenters.
            foreach(ActiveSite asite in PlugIn.ModelCore.Landscape)
            {
                if (agent.OutbreakZone[asite] == Zone.Lastzone)
                {
                    firstIteration = false;
                    continue;
                }
                if ((SiteVars.TimeOfLastEvent[asite] == (PlugIn.ModelCore.CurrentTime - 1)) && (SiteVars.AgentName[asite] == agent.AgentName))
                {
                    firstIteration = false;
                    continue;
                }
            }

            //Generate New Epicenters based on the location of past outbreaks
            //and the vulnerability of the current landscape:
            List<Location> oldZoneSiteList = new List<Location>(0);
            List<Location> outsideSiteList = new List<Location>(0);


            //---------------------------------------------------------
            //Count the number of potential new inside and outside epicenters:
            int totalInOut = 0;
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                if ((agent.Severity[site] >= agent.OutbreakEpicenterThresh) || ((SiteVars.TimeOfLastEvent[site] == (PlugIn.ModelCore.CurrentTime - 1)) && (SiteVars.AgentName[site] == agent.AgentName) && (SiteVars.BDASeverity[site] >= agent.OutbreakEpicenterThresh)))
                {
                    totalInOut++;
                    numInside++;//potential new epicenter inside last OutbreakZone
                    oldZoneSiteList.Add(site.Location);
                    //PlugIn.ModelCore.Log.WriteLine("  Severity = {0}.  Zone = {1}.", agent.Severity[site], agent.OutbreakZone[site]);
                }
                else
                {
                    if ((agent.OutbreakZone[site] == Zone.Nozone) && (SiteVars.Vulnerability[site] >= agent.EpidemicThresh)) //potential new epicenter
                    {
                        totalInOut++;
                        numOutside++;//potential new epicenter outside last OutbreakZone
                        outsideSiteList.Add(site.Location);
                    }
                }
            }


            PlugIn.ModelCore.UI.WriteLine("   Potential Number of Epicenters, Inside = {0}; Outside={1}, total={2}.", numInside, numOutside, totalInOut);

            //---------------------------------------------------------
            //Calculate number of Epicenters that will occur
            //INSIDE the last epidemic outbreak area.
            //This always occurs after the first iteration.
            //PlugIn.ModelCore.Log.WriteLine("   Adding epicenters INSIDE last outbreak zone.");
            oldZoneSiteList = PlugIn.ModelCore.shuffle(oldZoneSiteList);

            numInside = (int)((double)numInside * agent.OutbreakEpicenterCoeff);

            int listIndex = 0;
            if (oldZoneSiteList.Count > 0)
            {
                while (numInside > 0)
                {
                    newSiteList.Add(oldZoneSiteList[listIndex]);
                    epicenterNum++;
                    numInside--;
                    listIndex++;
                } //endwhile
            }

            //---------------------------------------------------------
            //SeedEpicenter determines if new epicenters will seed new outbreaks
            //OUTSIDE of previous outbreak zones.
            if (agent.SeedEpicenter)
            {
                PlugIn.ModelCore.UI.WriteLine("Adding epicenters OUTSIDE last outbreak zone.");


                outsideSiteList = PlugIn.ModelCore.shuffle(outsideSiteList);
                // Michaelis-Menton curve
                double propVuln = (double)numOutside / (double)PlugIn.ModelCore.Landscape.ActiveSiteCount;
                numOutside = (int)System.Math.Round((agent.SeedEpicenterMax * propVuln) / (agent.SeedEpicenterCoeff + propVuln));

                //PlugIn.ModelCore.Log.WriteLine("   Actual Number Outside = {0}.", numOutside);

                listIndex = 0;
                if (outsideSiteList.Count > 0)
                {
                    while (numOutside > 0)
                    {
                        newSiteList.Add(outsideSiteList[listIndex]);
                        epicenterNum++;
                        numOutside--;
                        listIndex++;
                    } //endwhile
                }
            }

            //If necessary, create list from scratch without
            //consideration of previous outbreaks.
            if (firstIteration)
                {
                int i, j;

                while (epicenterNum < oldEpicenterNum)
                {
                    i = (int)(PlugIn.ModelCore.GenerateUniform() * numRows) + 1;
                    j = (int)(PlugIn.ModelCore.GenerateUniform() * numCols) + 1;

                    Site site = PlugIn.ModelCore.Landscape[i, j];
                    if (site != null && site.IsActive)
                    {
                        newSiteList.Add(site.Location);
                        epicenterNum++;
                    }
                }
                //PlugIn.ModelCore.Log.WriteLine("   No Prior Outbreaks OR No available sites within prior outbreaks.  EpicenterNum = {0}.", newSiteList.Count);
            }

            agent.EpicenterNum = epicenterNum;

            //Generate NEW outbreak zones
            SpreadEpicenters(agent, newSiteList, BDAtimestep);

            return;
        }

        //---------------------------------------------------------------------
        ///<summary>
        ///Spread from Epicenters to outbreak zone using either a fixed radius method
        ///or a percolation method with variable neighborhood sizes.
        ///</summary>
        //---------------------------------------------------------------------
        private static void SpreadEpicenters(IAgent agent,
                                            List<Location> iSites,
                                            int BDAtimestep)
        {
            //PlugIn.ModelCore.Log.WriteLine("Spreading to New Epicenters.  There are {0} initiation sites.", iSites.Count);

            if(iSites == null)
                PlugIn.ModelCore.UI.WriteLine("ERROR:  The newSiteList is empty.");
            int dispersalDistance = agent.DispersalRate * BDAtimestep;

            foreach(Location siteLocation in iSites)
            {
                Site initiationSite = PlugIn.ModelCore.Landscape.GetSite(siteLocation);

                if(agent.DispersalTemp == DispersalTemplate.MaxRadius)
                {

                    foreach (RelativeLocation relativeLoc in agent.DispersalNeighbors)
                    {
                        Site neighbor = initiationSite.GetNeighbor(relativeLoc);
                        if (neighbor != null && neighbor.IsActive)
                            agent.OutbreakZone[neighbor] = Zone.Newzone;
                    }
                }
                if(agent.DispersalTemp != DispersalTemplate.MaxRadius)
                {
                    //PlugIn.ModelCore.Log.WriteLine("   Begin Percolation Spread to Neighbors.");
                    System.Collections.Queue sitesToConsider = new System.Collections.Queue();
                    sitesToConsider.Enqueue(initiationSite);

                    while (sitesToConsider.Count > 0 )
                    {
                        Site site = (Site)sitesToConsider.Dequeue();
                        agent.OutbreakZone[site] = Zone.Newzone;

                        foreach (RelativeLocation relativeLoc in agent.DispersalNeighbors)
                        {
                            Site neighbor = site.GetNeighbor(relativeLoc);

                            //Do not spread to inactive neighbors:
                            if(neighbor == null || !neighbor.IsActive)
                                continue;
                            //Do NOT spread to neighbors that have already been targeted for
                            //disturbance:
                            if (agent.OutbreakZone[neighbor] == Zone.Newzone)
                                continue;
                            //Check for duplicates:
                            if (sitesToConsider.Contains(neighbor))
                                continue;

                             if (DistanceBetweenSites(neighbor, initiationSite) <= dispersalDistance)
                               {
                                sitesToConsider.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }

        }

        //-------------------------------------------------------
        ///<summary>
        ///Calculate the distance between two Sites
        ///</summary>
        public static double DistanceBetweenSites(Site a, Site b)
        {

            int Col = (int) a.Location.Column - (int) b.Location.Column;
            int Row = (int) a.Location.Row - (int) b.Location.Row;

            double aSq = System.Math.Pow(Col,2);
            double bSq = System.Math.Pow(Row,2);
            return (System.Math.Sqrt(aSq + bSq) * (double) PlugIn.ModelCore.CellLength);

        }

    }
}
