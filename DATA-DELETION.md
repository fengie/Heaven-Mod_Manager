# Data Deletion and Local Reset

**Last updated:** October 3, 2026

The current application has no first-party cloud account database. Manager data is primarily local, so deletion is a local self-service operation rather than a server-side "deletion request."

## Before deleting anything

1. Close the manager and the game.
2. Back up profiles or state you may want to restore.
3. Do **not** delete your `Mods` or archive folders unless you intentionally want to remove your mod library.

## Manager-owned data

Use **Settings → Privacy & legal → Open app data folder** to open the manager state location for the active game. The exact location can vary with configuration.

The manager state can include the SQLite database, provider metadata, updater state, diagnostics, logs, snapshots, optional API credentials, and other recovery/configuration data.

To perform a full local reset, close the application and remove the manager state directory shown by the app. On the next launch, the manager will recreate required state and you may need to reconfigure games, profiles, provider credentials, and preferences.

## Diagnostics only

If you only want to remove diagnostics, delete the manager's generated log/support files after the app is closed. Support bundles are ordinary files you control and can delete wherever you saved them.

## Durable mod data is separate

Installed source packages, archives, and user-created mod content are intentionally treated as durable data. The app's storage-reclaim action does not delete those roots, and a privacy reset should not silently erase them.

## Data you voluntarily shared elsewhere

If you manually uploaded a support bundle, issue attachment, email, or other file to a third-party service, deleting your local copy does not delete that third-party copy. Use that service's deletion controls or contact the recipient.

## Future hosted services

If a future release introduces a first-party account or hosted personal-data service, the project must add an authenticated server-side access/deletion workflow and update this document before launch.
