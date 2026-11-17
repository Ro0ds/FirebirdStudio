# Infrastructure

External integrations belong here.

The Firebird ADO.NET provider is referenced here only. Do not leak provider-specific
types (`FbConnection`, `FbCommand`, etc.) into Core or Application.

The provider is distributed under the Initial Developer's Public License (IDPL) in
its upstream repository. This is not MIT. Review and preserve its license and
obligations before redistributing binaries or modified provider code. The current
project does not modify or vendor provider source code.
