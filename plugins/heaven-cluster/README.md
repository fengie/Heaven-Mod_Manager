# Heaven Cluster

Multi-machine orchestration without coupling scheduling to a specific transport.

Workers register an opaque endpoint, capabilities, labels, capacity, and heartbeat. Selection is capability-aware, stale/paused/full workers are excluded, preferred labels are honored, and load breaks ties. Capacity reservations are atomic SQLite transactions. A caller-supplied dispatcher can then use Heaven Bridge, SSH, or a future authenticated transport.

Validate with `python .\plugins\heaven-cluster\verify.py`.
