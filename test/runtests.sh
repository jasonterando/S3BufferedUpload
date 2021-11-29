#!/bin/sh
mkdir -p .results
rm -r -f .results/*
# dotnet test --collect:"XPlat Code Coverage" -r .results/log -l trx
dotnet test /p:CollectCoverage=true /p:CoverletOutput='.results/coverage.xml' /p:CoverletOutputFormat=opencover /p:Threshold=\"90,80,90\" /p:ThresholdType=\"line,branch,method\"
result=$?
dotnet reportgenerator -reports:`find .results -name coverage.xml` -targetdir:.results/reports
if [ $result = 0 ]; then
    echo "All tests passed and coverage is acceptable!"
else
    echo "Some tests failed and/or coverage is not acceptable :("
fi
exit $result