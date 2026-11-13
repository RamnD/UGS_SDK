# Version control, shared rules

The route picks the system: Git or Plastic. Branches, publishing, and protections are in that system's document.

Before editing, the current branch, working tree, and existing diff are visible. Someone else's change is not reset, hidden, or included in this change.

One writer works a task branch at a time. Parallel work uses another branch and another working tree.

One change is one coherent edit. The message describes the result.

Publishing happens only when explicitly asked. A request to do the task does not publish it and does not promote it to the acceptance or release branch.

A merge into the integration branch is allowed when the task explicitly allows it, the check for that revision is green, and a separate pass that did not write the change has reviewed the same SHA. Another agent qualifies. The report names the reviewer and the SHA. That record is the pass. A GitHub approval is a separate event, described in the Git document, and one shared agent account does not supply it. A human merges into acceptance and release.

A permanent branch named after an agent is not created.
