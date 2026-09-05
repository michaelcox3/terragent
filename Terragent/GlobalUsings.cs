// The folders are the layering, and the namespaces follow them
// so a type's kind is visible at its declaration. They are imported globally rather
// than file by file: the layering is enforced by the boundary scan, which reads what a
// file actually references, and forty-eight import lists would only be a second,
// weaker copy of that rule.
global using Terragent.Act;
global using Terragent.Follow;
global using Terragent.Do;
global using Terragent.Plan;
global using Terragent.Search;
global using Terragent.Sense;
global using Terragent.Report;
global using Terragent.Display;
global using Terragent.World;
