"""Heaven Control Plane public surface."""

from .indexing import IndexCapabilityProvider, RepositoryIndex
from .protocol import PLUGIN_VERSION, SCHEMA_VERSION, CapabilitySpec, ControlPlaneError
from .registry import CapabilityRegistry, build_registry
from .service import HeavenControlPlane

__all__ = [
    "PLUGIN_VERSION",
    "SCHEMA_VERSION",
    "CapabilitySpec",
    "ControlPlaneError",
    "CapabilityRegistry",
    "RepositoryIndex",
    "IndexCapabilityProvider",
    "build_registry",
    "HeavenControlPlane",
]
