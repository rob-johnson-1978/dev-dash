// output of the single-file build
#:property OutputPath=./app

// this would be a nuget reference in your case
#:project ../../src/DevDash/DevDash.csproj

// run
DevDash.Bootstrapping.RunDevDash(args);