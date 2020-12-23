
For HTTP1: 
```
dotnet run CosmosBenchmark.csproj -w Echo11Server -e https://localhost:8080 --database db1 --container c1 -n 10000 --pl 10
```

For HTTP2: 
```
dotnet run CosmosBenchmark.csproj -w Echo20Server -e https://localhost:8081 --database db1 --container c1 -n 10000 --pl 10
```
