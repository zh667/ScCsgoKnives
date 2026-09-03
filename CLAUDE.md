# Build deliverables

Use the project-root `output/` directory for final deliverables that the user should receive on Windows.

After a successful release build, copy the final installable `.scmod` package from the build directory into `output/`. Use a clear, versioned filename when a version is available. Build intermediates in `bin/`, `obj/`, `.vs/`, or `.tmp-*` are peer-local and must not be treated as delivered artifacts.
