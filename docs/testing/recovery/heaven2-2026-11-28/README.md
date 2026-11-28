# Recovered acceptance probe sources

These six probes cover workshop projection, mod families and archive intake.
They were rebuilt with .NET 10 against the historical source revisions listed
in the manifest. The project references point into a Manager source checkout.
For exact reproduction, place the recovered probe directory in the layout
shown here at the matching historical revision.

Use isolated copies of game and Manager state. Some probes write fixture state
or extract archives. Keep user data, mod assets and run evidence outside Git.

The read-only catalog verifier accepts a scratch catalog, source root and local
expectations file. Keep the expectations, inputs and result outside Git.
