sudo docker kill atlas-y-databasewebapi
sudo docker rm atlas-y-databasewebapi
sudo docker rmi minmuslin/atlas-y-databasewebapi
sudo docker pull minmuslin/atlas-y-databasewebapi
sudo docker run -d --name atlas-y-databasewebapi --privileged -p 5101:8080 minmuslin/atlas-y-databasewebapi