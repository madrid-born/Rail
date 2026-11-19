FROM eclipse-temurin:25-jre
WORKDIR /opt/graphhopper
ADD https://repo.maven.apache.org/maven2/com/graphhopper/graphhopper-web/11.1/graphhopper-web-11.1.jar graphhopper.jar
COPY config.yml config.yml
# The container listens internally; compose exposes only host loopback.
RUN sed -i 's/bind_host: 127.0.0.1/bind_host: 0.0.0.0/g' config.yml
EXPOSE 8989
CMD ["java", "-Xms128m", "-Xmx1g", "-jar", "graphhopper.jar", "server", "config.yml"]
