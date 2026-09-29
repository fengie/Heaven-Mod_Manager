# Heaven Cluster

Multi-machine orchestration without coupling scheduling to a specific transport.

Workers register an opaque endpoint, capabilities, labels, capacity, and heartbeat. Selection is capability-aware, stale/paused/full workers are excluded, preferred labels are honored, and load breaks ties. Capacity reservations are atomic SQLite transactions.

## Queue integration

`QueueClusterDispatcher` composes the existing `heaven-task-queue` and `ClusterScheduler` instead of duplicating either store. One dispatch iteration:

1. atomically claims one runnable queue task;
2. reads bounded `required_capabilities`, `preferred_labels`, and `resources` from the task payload;
3. acquires named queue resource locks;
4. selects and atomically reserves a healthy compatible cluster worker;
5. renews the queue lease while the caller-supplied transport runs;
6. persists a bounded execution receipt through the queue;
7. releases worker capacity and resources on completion, cancellation, quota failure, or transport error.

The dispatcher never stores transport credentials. Queue payloads/results must remain secret-free; future privileged transports should use opaque secret handles from the shared security layer.

Validate with `python .\plugins\heaven-cluster\verify.py`.
