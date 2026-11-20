## Summary
- Describe what changed and why.

## Verification
- [ ] `$env:APP_ISOLATED_TEST = "1"`
- [ ] `dotnet build Dev\reEngine.slnx -c Release /warnaserror` (`0` warnings)
- [ ] `dotnet test Dev\reEngine.slnx -c Release --no-build`
- [ ] `dotnet build Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true /warnaserror` (`0` warnings)
- [ ] `dotnet test Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true --no-build`
- [ ] Zero copyrighted media/BIOS files, PII paths, or Release `Foundation.Automation` references introduced
