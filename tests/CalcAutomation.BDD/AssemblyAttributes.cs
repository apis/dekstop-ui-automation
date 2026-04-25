// UIA3 requires STA; no parallel execution allowed with a single Calculator window
[assembly: NUnit.Framework.Apartment(System.Threading.ApartmentState.STA)]
[assembly: NUnit.Framework.Parallelizable(NUnit.Framework.ParallelScope.None)]
