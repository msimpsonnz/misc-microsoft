Minecraft
```
az container create --resource-group Containers --name mjsdemo-aci-minecraft --image openhack/minecraft-server --dns-name-label mjsdemo-aci-minecraft --ports 25565 --environment-variables EULA=TRUE
```
```
az container delete --resource-group Containers --name mjsdemo-aci-minecraft
```


Mesh
``
az group create --name Mesh --location eastus 
``
```
az mesh deployment create --resource-group Mesh --template-uri https://raw.githubusercontent.com/msimpsonnz/openhackSF/master/mesh/minecraft.json --parameters "{'location': {'value': 'eastus'}}"
```

```
az mesh deployment create --resource-group Mesh --template-file minecraft.json --parameters "{'location': {'value': 'eastus'}}"
```

Mesh Windows
40.87.82.108
```
az mesh deployment create --resource-group Mesh --template-uri https://raw.githubusercontent.com/msimpsonnz/openhackSF/master/mesh/minecraftWin.json --parameters "{'location': {'value': 'eastus'}}"
```

```
az mesh deployment create --resource-group Mesh --template-file minecraftWin.json --parameters "{'location': {'value': 'eastus'}}"
```